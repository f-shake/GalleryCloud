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
        [FromQuery] double? lat1,
        [FromQuery] double? lng1,
        [FromQuery] double? lat2,
        [FromQuery] double? lng2,
        [FromQuery] bool? isDeleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 2000)
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

        // Bounding box filter (map area)
        if (lat1.HasValue && lng1.HasValue && lat2.HasValue && lng2.HasValue)
        {
            var south = Math.Min(lat1.Value, lat2.Value);
            var north = Math.Max(lat1.Value, lat2.Value);
            var west = Math.Min(lng1.Value, lng2.Value);
            var east = Math.Max(lng1.Value, lng2.Value);
            query = query.Where(p => p.Latitude != null && p.Longitude != null
                && p.Latitude >= south && p.Latitude <= north
                && p.Longitude >= west && p.Longitude <= east);
        }

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

    [HttpGet("filters")]
    public async Task<IActionResult> GetFilters()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var formats = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted))
            .Where(p => p.FileFormat != null && p.FileFormat != "")
            .Select(p => p.FileFormat)
            .Distinct()
            .OrderBy(f => f)
            .ToListAsync();

        var devices = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted))
            .Where(p => p.DeviceModel != null && p.DeviceModel != "")
            .Select(p => p.DeviceModel!)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();

        var tags = await _db.Tags
            .Where(t => t.UserId == _userContext.UserId)
            .Select(t => new TagItem(t.Id, t.Name, t.Color))
            .ToListAsync();

        return Ok(new SearchFilterOptions(formats, devices, tags));
    }
}
