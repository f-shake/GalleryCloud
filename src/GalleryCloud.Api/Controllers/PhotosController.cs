using GalleryCloud.Api.Data;
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

    public PhotosController(AppDbContext db, UserContext userContext, IThumbnailService? thumbnailService = null)
    {
        _db = db;
        _userContext = userContext;
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
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted)
            .OrderByDescending(p => p.TakenAt);

        var total = await query.CountAsync();
        var photos = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(p => new
            {
                p.Id, p.FileName, p.FileFormat,
                p.Width, p.Height, p.Orientation,
                p.TakenAt, p.DeviceModel,
                p.Latitude, p.Longitude,
                p.FileSize, p.CreatedAt
            })
            .ToListAsync();

        return Ok(new { total, page, limit, photos });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted);

        if (photo == null)
            return NotFound();

        return Ok(new
        {
            photo.Id, photo.FileName, photo.FileFormat, photo.FilePath,
            photo.Width, photo.Height, photo.Orientation,
            photo.TakenAt, photo.DeviceModel,
            photo.Latitude, photo.Longitude,
            photo.FileSize, photo.Md5Hash,
            photo.CreatedAt, photo.UpdatedAt
        });
    }

    [HttpGet("{id}/file")]
    public async Task<IActionResult> GetFile(string id)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted);

        if (photo == null)
            return NotFound();

        var fullPath = Path.GetFullPath(Path.Combine(_userContext.RootPath, photo.FilePath));

        // Path traversal check
        if (!fullPath.StartsWith(Path.GetFullPath(_userContext.RootPath), StringComparison.OrdinalIgnoreCase))
            return Forbid();

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { error = "File not found on disk" });

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
            return StatusCode(500, new { error = "Thumbnail service not available" });

        var thumbSize = size switch
        {
            "grid" => ThumbnailSize.Grid,
            "preview" => ThumbnailSize.Preview,
            "full" => ThumbnailSize.Full,
            _ => ThumbnailSize.Grid
        };

        var stream = await _thumbnailService.GetThumbnailAsync(id, thumbSize, w, HttpContext.RequestAborted);
        if (stream == null)
            return NotFound(new { error = "Photo not found or unsupported format" });

        return File(stream, "image/webp");
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
        return Ok(new { message = "Deleted" });
    }

    [HttpPatch("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && p.IsDeleted);

        if (photo == null)
            return NotFound();

        var fullPath = Path.GetFullPath(Path.Combine(_userContext.RootPath, photo.FilePath));
        if (!System.IO.File.Exists(fullPath))
            return BadRequest(new { error = "Original file no longer exists" });

        photo.IsDeleted = false;
        photo.DeletedAt = null;
        photo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Restored" });
    }

    public record MoveRequest(string NewRelativePath);

    [HttpPut("{id}/move")]
    public async Task<IActionResult> Move(string id, [FromBody] MoveRequest request)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted);

        if (photo == null)
            return NotFound();

        var oldFullPath = Path.GetFullPath(Path.Combine(_userContext.RootPath, photo.FilePath));
        var newFullPath = Path.GetFullPath(Path.Combine(_userContext.RootPath, request.NewRelativePath));

        if (!newFullPath.StartsWith(Path.GetFullPath(_userContext.RootPath), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Invalid destination path" });

        if (!System.IO.File.Exists(oldFullPath))
            return BadRequest(new { error = "Source file not found" });

        Directory.CreateDirectory(Path.GetDirectoryName(newFullPath)!);
        System.IO.File.Move(oldFullPath, newFullPath);

        photo.FilePath = request.NewRelativePath;
        photo.FileName = Path.GetFileName(request.NewRelativePath);
        photo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Moved" });
    }

    public record RenameRequest(string NewFileName);

    [HttpPatch("{id}/rename")]
    public async Task<IActionResult> Rename(string id, [FromBody] RenameRequest request)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == _userContext.UserId && !p.IsDeleted);

        if (photo == null)
            return NotFound();

        var oldFullPath = Path.GetFullPath(Path.Combine(_userContext.RootPath, photo.FilePath));
        var dir = Path.GetDirectoryName(photo.FilePath) ?? "";
        var newRelativePath = Path.Combine(dir, request.NewFileName);
        var newFullPath = Path.GetFullPath(Path.Combine(_userContext.RootPath, newRelativePath));

        if (!newFullPath.StartsWith(Path.GetFullPath(_userContext.RootPath), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Invalid file name" });

        if (!System.IO.File.Exists(oldFullPath))
            return BadRequest(new { error = "Source file not found" });

        System.IO.File.Move(oldFullPath, newFullPath);

        photo.FilePath = newRelativePath;
        photo.FileName = request.NewFileName;
        photo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Renamed" });
    }
}
