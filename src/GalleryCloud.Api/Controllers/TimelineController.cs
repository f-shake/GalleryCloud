using GalleryCloud.Api.Data;
using GalleryCloud.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/timeline")]
public class TimelineController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;

    public TimelineController(AppDbContext db, UserContext userContext)
    {
        _db = db;
        _userContext = userContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetTimeline(
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 50,
        [FromQuery] string groupLevel = "day")
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var query = _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt != null)
            .OrderByDescending(p => p.TakenAt);

        switch (groupLevel)
        {
            case "day":
                return await GetDayGroups(query, cursor, limit);
            case "month":
                return await GetMonthGroups(query, cursor, limit);
            default:
                return await GetFlatList(query, cursor, limit);
        }
    }

    private async Task<IActionResult> GetDayGroups(IQueryable<Core.Entities.Photo> query, string? cursor, int limit)
    {
        var groups = new List<object>();
        DateTime? cursorDate = null;
        string? nextCursor = null;

        if (!string.IsNullOrEmpty(cursor) && DateTime.TryParse(cursor, out var cdt))
            cursorDate = cdt;

        // Fetch photos until we have enough groups or run out
        var offset = 0;
        var batchSize = 200;
        var currentDay = string.Empty;
        var dayPhotos = new List<object>();
        var reachCursor = cursorDate == null;

        while (groups.Count < limit)
        {
            var batch = await query.Skip(offset).Take(batchSize).Select(p => new
            {
                p.Id, p.FileName, p.FileFormat, p.Width, p.Height, p.Orientation,
                p.TakenAt, p.Latitude, p.Longitude, p.FileSize
            }).ToListAsync();

            if (batch.Count == 0) break;

            foreach (var p in batch)
            {
                var day = p.TakenAt?.ToString("yyyy-MM-dd") ?? "unknown";

                if (!reachCursor)
                {
                    if (day == cursor) reachCursor = true;
                    continue;
                }

                if (day != currentDay)
                {
                    if (dayPhotos.Count > 0)
                    {
                        groups.Add(new { label = FormatDayLabel(currentDay), cursor = currentDay, photos = dayPhotos });
                        dayPhotos = new List<object>();
                    }
                    currentDay = day;
                }

                dayPhotos.Add(p);

                if (groups.Count >= limit) break;
            }

            offset += batchSize;
            if (offset > 10000) break; // safety limit
        }

        // Don't forget the last day
        if (dayPhotos.Count > 0 && groups.Count < limit)
        {
            groups.Add(new { label = FormatDayLabel(currentDay), cursor = currentDay, photos = dayPhotos });
        }

        if (groups.Count > 0)
            nextCursor = ((dynamic)groups[^1]).cursor;

        var hasMore = groups.Count >= limit;

        return Ok(new { groups, nextCursor, hasMore });
    }

    private async Task<IActionResult> GetMonthGroups(IQueryable<Core.Entities.Photo> query, string? cursor, int limit)
    {
        // Simplified: batch by month
        var photos = await query.Take(5000).Select(p => new
        {
            p.Id, p.FileName, p.FileFormat, p.Width, p.Height, p.Orientation,
            p.TakenAt, p.Latitude, p.Longitude, p.FileSize
        }).ToListAsync();

        var grouped = photos
            .GroupBy(p => p.TakenAt?.ToString("yyyy-MM") ?? "unknown")
            .Select(g => new
            {
                label = FormatMonthLabel(g.Key),
                cursor = g.Key,
                photos = g.Take(200).ToList()
            })
            .ToList();

        var groups = grouped.Take(limit).ToList<object>();
        var nextCursor = groups.Count > 0 ? ((dynamic)groups[^1]).cursor : null;

        return Ok(new { groups, nextCursor, hasMore = groups.Count >= limit });
    }

    private async Task<IActionResult> GetFlatList(IQueryable<Core.Entities.Photo> query, string? cursor, int limit)
    {
        int offsetNum = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var c)) offsetNum = c;

        var photos = await query.Skip(offsetNum).Take(limit).Select(p => new
        {
            p.Id, p.FileName, p.FileFormat, p.Width, p.Height, p.Orientation,
            p.TakenAt, p.Latitude, p.Longitude, p.FileSize
        }).ToListAsync();

        var hasMore = photos.Count >= limit;
        var groups = new List<object> { new { photos } };

        return Ok(new { groups, nextCursor = hasMore ? (offsetNum + limit).ToString() : null, hasMore });
    }

    [HttpGet("years")]
    public async Task<IActionResult> GetYears()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var years = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt != null)
            .GroupBy(p => p.TakenAt!.Value.Year)
            .Select(g => new { year = g.Key, count = g.Count() })
            .OrderByDescending(x => x.year)
            .ToListAsync();

        return Ok(years);
    }

    [HttpGet("density")]
    public async Task<IActionResult> GetDensity()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var density = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt != null)
            .GroupBy(p => new { p.TakenAt!.Value.Year, p.TakenAt!.Value.Month })
            .Select(g => new { year = g.Key.Year, month = g.Key.Month, count = g.Count() })
            .OrderBy(x => x.year).ThenBy(x => x.month)
            .ToListAsync();

        // Pivot to { year, monthCounts: [12] }
        var result = density.GroupBy(d => d.year).Select(g => new
        {
            year = g.Key,
            monthCounts = Enumerable.Range(1, 12).Select(m =>
                g.FirstOrDefault(x => x.month == m)?.count ?? 0).ToArray()
        }).ToList();

        return Ok(result);
    }

    private static string FormatDayLabel(string day)
    {
        if (DateTime.TryParse(day, out var dt))
            return $"{dt.Month}月{dt.Day}日";
        return day;
    }

    private static string FormatMonthLabel(string month)
    {
        if (DateTime.TryParse(month + "-01", out var dt))
            return $"{dt.Year}年{dt.Month}月";
        return month;
    }
}
