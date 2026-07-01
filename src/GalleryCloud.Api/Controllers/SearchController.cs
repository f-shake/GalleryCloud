using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;

    public SearchController(AppDbContext db, UserContext userContext)
    {
        _db = db;
        _userContext = userContext;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? format,
        [FromQuery] string? device,
        [FromQuery] string? tag,
        [FromQuery] bool? isDeleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var query = _db.Photos.Where(p => p.UserId == _userContext.UserId
            && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted));

        // Soft delete filter
        if (isDeleted == true)
            query = query.Where(p => p.IsDeleted);
        else
            query = query.Where(p => !p.IsDeleted);

        // Text search on file name
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => EF.Functions.Like(p.FileName, $"%{q}%"));

        // Date range
        if (!string.IsNullOrWhiteSpace(from) && DateTime.TryParse(from, out var fromDate))
            query = query.Where(p => p.TakenAt >= fromDate);
        if (!string.IsNullOrWhiteSpace(to) && DateTime.TryParse(to, out var toDate))
            query = query.Where(p => p.TakenAt <= toDate);

        // Format filter (comma separated)
        if (!string.IsNullOrWhiteSpace(format))
        {
            var formats = format.Split(',').Select(f => f.Trim().ToLowerInvariant()).ToList();
            query = query.Where(p => formats.Contains(p.FileFormat));
        }

        // Device filter
        if (!string.IsNullOrWhiteSpace(device))
            query = query.Where(p => p.DeviceModel != null && p.DeviceModel.Contains(device));

        // Tag filter
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagEntity = await _db.Tags
                .FirstOrDefaultAsync(t => t.UserId == _userContext.UserId && t.Name == tag);
            if (tagEntity != null)
            {
                var photoIds = await _db.PhotoTags
                    .Where(pt => pt.TagId == tagEntity.Id)
                    .Select(pt => pt.PhotoId)
                    .ToListAsync();
                query = query.Where(p => photoIds.Contains(p.Id));
            }
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.TakenAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(p => new PhotoListItem(
                p.Id, p.FileName, p.FileFormat,
                p.Width, p.Height, p.Orientation,
                p.TakenAt, p.Latitude, p.Longitude,
                p.FileSize, p.FilePath, p.IsDeleted,
                p.DeviceModel
            ))
            .ToListAsync();

        return Ok(new SearchResponse(total, page, limit, items));
    }
}
