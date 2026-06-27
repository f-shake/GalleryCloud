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

        return groupLevel switch
        {
            "day" => await GetDayGroups(query, cursor, limit),
            "month" => await GetMonthGroups(query, cursor, limit),
            _ => await GetFlatList(query, cursor, limit),
        };
    }

    private async Task<IActionResult> GetDayGroups(IOrderedQueryable<Core.Entities.Photo> query, string? cursor, int limit)
    {
        var groups = new List<object>();
        DateTime? cursorDate = null;

        if (!string.IsNullOrEmpty(cursor) && DateTime.TryParse(cursor, out var cdt))
            cursorDate = cdt.Date;

        var batchSize = 500;
        var offset = 0;
        var currentDay = "";
        var dayPhotos = new List<object>();
        var pastCursor = cursorDate == null;

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
                var takenAt = p.TakenAt;

                // Skip until past cursor date (use date comparison, not string match)
                if (!pastCursor)
                {
                    if (takenAt.HasValue && takenAt.Value.Date >= cursorDate!.Value)
                        continue;
                    pastCursor = true;
                }

                var day = takenAt?.ToString("yyyy-MM-dd") ?? "";

                if (day != currentDay)
                {
                    if (dayPhotos.Count > 0)
                    {
                        groups.Add(new { label = FormatDayLabel(currentDay), cursor = currentDay, photos = dayPhotos });
                        dayPhotos = new List<object>();
                        if (groups.Count >= limit) break;
                    }
                    currentDay = day;
                }

                dayPhotos.Add(p);
            }

            if (groups.Count >= limit) break;
            offset += batchSize;
        }

        // Last day
        if (dayPhotos.Count > 0 && groups.Count < limit)
            groups.Add(new { label = FormatDayLabel(currentDay), cursor = currentDay, photos = dayPhotos });

        var nextCursor = groups.Count > 0 ? ((dynamic)groups[^1]).cursor : null;

        return Ok(new { groups, nextCursor, hasMore = groups.Count >= limit });
    }

    private async Task<IActionResult> GetMonthGroups(IOrderedQueryable<Core.Entities.Photo> query, string? cursor, int limit)
    {
        var allPhotos = await query.Select(p => new
        {
            p.Id, p.FileName, p.FileFormat, p.Width, p.Height, p.Orientation,
            p.TakenAt, p.Latitude, p.Longitude, p.FileSize
        }).ToListAsync();

        var grouped = allPhotos
            .GroupBy(p => p.TakenAt?.ToString("yyyy-MM") ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new
            {
                label = FormatMonthLabel(g.Key),
                cursor = g.Key,
                photos = g.Take(300).Cast<object>().ToList()
            })
            .ToList();

        // Apply cursor
        var startIdx = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            var idx = grouped.FindIndex(g => g.cursor == cursor);
            if (idx >= 0) startIdx = idx + 1;
        }

        var result = grouped.Skip(startIdx).Take(limit).ToList<object>();
        var nextCursor = result.Count > 0 ? ((dynamic)result[^1]).cursor : null;

        return Ok(new { groups = result, nextCursor, hasMore = startIdx + limit < grouped.Count });
    }

    private async Task<IActionResult> GetFlatList(IOrderedQueryable<Core.Entities.Photo> query, string? cursor, int limit)
    {
        int offsetNum = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var c)) offsetNum = c;

        var photos = await query.Skip(offsetNum).Take(limit).Select(p => new
        {
            p.Id, p.FileName, p.FileFormat, p.Width, p.Height, p.Orientation,
            p.TakenAt, p.Latitude, p.Longitude, p.FileSize
        }).ToListAsync();

        var hasMore = photos.Count >= limit;
        return Ok(new
        {
            groups = new[] { new { photos = photos.Cast<object>().ToList() } },
            nextCursor = hasMore ? (offsetNum + limit).ToString() : null,
            hasMore
        });
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

        var result = density.GroupBy(d => d.year).Select(g => new
        {
            year = g.Key,
            monthCounts = Enumerable.Range(1, 12).Select(m =>
                g.FirstOrDefault(x => x.month == m)?.count ?? 0).ToArray()
        }).ToList();

        return Ok(result);
    }

    private static string FormatDayLabel(string d)
        => DateTime.TryParse(d, out var dt) ? $"{dt.Month}月{dt.Day}日" : d;

    private static string FormatMonthLabel(string m)
        => DateTime.TryParse(m + "-01", out var dt) ? $"{dt.Year}年{dt.Month}月" : m;
}
