using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Helpers;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Enums;
using GalleryCloud.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/photos")]
public class PhotosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;
    private readonly IThumbnailService? _thumbnailService;
    private readonly ISettingService _settingService;

    public PhotosController(AppDbContext db, UserContext userContext, ISettingService settingService, IThumbnailService? thumbnailService = null)
    {
        _db = db;
        _userContext = userContext;
        _settingService = settingService;
        _thumbnailService = thumbnailService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var query = _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted))
            .OrderByDescending(p => p.TakenAt);

        var total = await query.CountAsync();
        var photos = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(p => new PhotoItem(
                p.Id, p.FileName, p.FileFormat,
                p.Width, p.Height, p.Orientation,
                p.TakenAt, p.Latitude, p.Longitude,
                p.FileSize, p.DeviceModel, p.CreatedAt
            ))
            .ToListAsync();

        return Ok(new PhotoListResponse(total, page, limit, photos));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted));

        if (photo == null)
            return NotFound();

        return Ok(new PhotoDetail(
            photo.Id, photo.FileName, photo.FileFormat, photo.FilePath, photo.RootId,
            photo.Width, photo.Height, photo.Orientation,
            photo.TakenAt, photo.DeviceModel,
            photo.ExposureTime, photo.Iso, photo.Aperture,
            photo.FocalLength, photo.FocalLength35mm,
            photo.Latitude, photo.Longitude,
            photo.FileSize, photo.Md5Hash,
            photo.CreatedAt, photo.UpdatedAt
        ));
    }

    [HttpGet("ids")]
    public async Task<IActionResult> GetIds(
        [FromQuery] int? fromYear = null,
        [FromQuery] int? toYear = null)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var query = _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted))
            .OrderByDescending(p => p.TakenAt);

        if (fromYear.HasValue)
            query = (IOrderedQueryable<Core.Entities.Photo>)query.Where(p => p.TakenAt!.Value.Year >= fromYear.Value);
        if (toYear.HasValue)
            query = (IOrderedQueryable<Core.Entities.Photo>)query.Where(p => p.TakenAt!.Value.Year <= toYear.Value);

        // Return parallel flat arrays for compactness (avoids JSON object key repetition)
        var items = await query
            .Select(p => new { p.Id, p.TakenAt })
            .ToListAsync();

        var ids = new List<string>(items.Count);
        var dates = new List<int?>(items.Count);
        foreach (var item in items)
        {
            ids.Add(item.Id);
            dates.Add(item.TakenAt?.Year is int y
                ? y * 10000 + (item.TakenAt?.Month ?? 1) * 100 + (item.TakenAt?.Day ?? 1)
                : null);
        }

        return Ok(new PhotoIdsResponse(ids, dates));
    }

    [HttpGet("{id}/file")]
    public async Task<IActionResult> GetFile(string id)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted));

        if (photo == null)
            return NotFound();

        // Resolve full path via the photo's root
        var root = await _db.UserRoots.FindAsync(photo.RootId);
        if (root == null) return NotFound();

        var fullPath = Path.GetFullPath(Path.Combine(root.RootPath, photo.FilePath));
        if (!fullPath.StartsWith(Path.GetFullPath(root.RootPath), StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new ErrorResult("Forbidden"));

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new ErrorResult("File not found on disk"));

        var stream = System.IO.File.OpenRead(fullPath);
        var contentType = photo.FileFormat.ToLower() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".avif" => "image/avif",
            _ => "application/octet-stream"
        };

        return File(stream, contentType, photo.FileName);
    }

    [HttpGet("{id}/thumbnail")]
    public async Task<IActionResult> GetThumbnail(
        string id,
        [FromQuery] string size = "grid",
        [FromQuery] int w = 400)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        if (_thumbnailService == null)
            return StatusCode(500, new ErrorResult("Thumbnail service not available"));

        var thumbSize = size switch
        {
            "grid" => ThumbnailSize.Grid,
            "preview" => ThumbnailSize.Preview,
            _ => ThumbnailSize.Grid
        };

        // Preview: always generate synchronously (only one at a time, no queue needed)
        if (size == "preview")
        {
            var result = await _thumbnailService.GetThumbnailAsync(id, thumbSize, w, HttpContext.RequestAborted);
            if (result == null) return NotFound();
            return new FileContentResult(await StreamHelper.ReadFullyAsync(result), "image/webp");
        }

        // 1. Try cache — if hit, return bytes directly
        var cached = await _thumbnailService.TryGetCachedAsync(id, thumbSize, w);
        if (cached != null)
            return new FileContentResult(await StreamHelper.ReadFullyAsync(cached), "image/webp");

        // 2. Not cached — grid: enqueue background generation, return 202 immediately
        _thumbnailService.EnqueueAsync(id, thumbSize, w);
        return Accepted(new MessageResult("pending"));
    }

    [HttpPatch("batch/delete")]
    public async Task<IActionResult> BatchDelete([FromBody] BatchIdsRequest request)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photos = await _db.Photos
            .Where(p => request.Ids.Contains(p.Id) && p.UserId == _userContext.UserId && !p.IsDeleted)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var photo in photos)
        {
            photo.IsDeleted = true;
            photo.DeletedAt = now;
            photo.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();
        return Ok(new MessageResult($"Deleted {photos.Count} photos"));
    }

    [HttpPatch("batch/restore")]
    public async Task<IActionResult> BatchRestore([FromBody] BatchIdsRequest request)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photos = await _db.Photos
            .IgnoreQueryFilters()
            .Where(p => request.Ids.Contains(p.Id) && p.UserId == _userContext.UserId && p.IsDeleted)
            .ToListAsync();

        // Verify files still exist
        var roots = await _db.UserRoots.Where(r => r.UserId == _userContext.UserId && !r.IsDeleted).ToListAsync();
        var validIds = new HashSet<string>();
        foreach (var photo in photos)
        {
            var root = roots.FirstOrDefault(r => r.Id == photo.RootId);
            if (root == null) continue;
            var fullPath = Path.GetFullPath(Path.Combine(root.RootPath, photo.FilePath));
            if (System.IO.File.Exists(fullPath))
                validIds.Add(photo.Id);
        }

        var toRestore = photos.Where(p => validIds.Contains(p.Id)).ToList();
        foreach (var photo in toRestore)
        {
            photo.IsDeleted = false;
            photo.DeletedAt = null;
            photo.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new MessageResult($"Restored {toRestore.Count} photos"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted);

        if (photo == null)
            return NotFound();

        // Soft delete
        photo.IsDeleted = true;
        photo.DeletedAt = DateTime.UtcNow;
        photo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new MessageResult("Deleted"));
    }

    [HttpPatch("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && p.IsDeleted);

        if (photo == null)
            return NotFound();

        var root = await _db.UserRoots.FindAsync(photo.RootId);
        if (root == null)
            return BadRequest(new ErrorResult("Root no longer exists"));

        var fullPath = Path.GetFullPath(Path.Combine(root.RootPath, photo.FilePath));
        if (!System.IO.File.Exists(fullPath))
            return BadRequest(new ErrorResult("Original file no longer exists"));

        photo.IsDeleted = false;
        photo.DeletedAt = null;
        photo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new MessageResult("Restored"));
    }

    [HttpPut("{id}/move")]
    public async Task<IActionResult> Move(string id, [FromBody] MoveRequest request)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted);

        if (photo == null)
            return NotFound();

        var root = await _db.UserRoots.FindAsync(photo.RootId);
        if (root == null) return NotFound();
        var rootPath = Path.GetFullPath(root.RootPath);

        var oldFullPath = Path.GetFullPath(Path.Combine(rootPath, photo.FilePath));
        var newFullPath = Path.GetFullPath(Path.Combine(rootPath, request.NewRelativePath));

        if (!newFullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ErrorResult("Invalid destination path"));

        if (!System.IO.File.Exists(oldFullPath))
            return BadRequest(new ErrorResult("Source file not found"));

        Directory.CreateDirectory(Path.GetDirectoryName(newFullPath)!);
        System.IO.File.Move(oldFullPath, newFullPath);

        photo.FilePath = request.NewRelativePath;
        photo.FileName = Path.GetFileName(request.NewRelativePath);
        photo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new MessageResult("Moved"));
    }

    [HttpPatch("{id}/rename")]
    public async Task<IActionResult> Rename(string id, [FromBody] RenameRequest request)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted);

        if (photo == null)
            return NotFound();

        var root = await _db.UserRoots.FindAsync(photo.RootId);
        if (root == null) return NotFound();
        var rootPath = Path.GetFullPath(root.RootPath);

        var oldFullPath = Path.GetFullPath(Path.Combine(rootPath, photo.FilePath));
        var dir = Path.GetDirectoryName(photo.FilePath) ?? "";
        var newRelativePath = Path.Combine(dir, request.NewFileName);
        var newFullPath = Path.GetFullPath(Path.Combine(rootPath, newRelativePath));

        if (!newFullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ErrorResult("Invalid file name"));

        if (!System.IO.File.Exists(oldFullPath))
            return BadRequest(new ErrorResult("Source file not found"));

        System.IO.File.Move(oldFullPath, newFullPath);

        photo.FilePath = newRelativePath;
        photo.FileName = request.NewFileName;
        photo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new MessageResult("Renamed"));
    }

}
