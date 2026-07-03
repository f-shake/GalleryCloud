using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
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
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt != null
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted))
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
        var groups = new List<TimelineGroup>();
        DateTime? cursorDate = null;

        if (!string.IsNullOrEmpty(cursor) && DateTime.TryParse(cursor, out var cdt))
            cursorDate = cdt.Date;

        var batchSize = 500;
        var offset = 0;
        var currentDay = "";
        var dayPhotos = new List<PhotoItem>();
        var pastCursor = cursorDate == null;

        while (groups.Count < limit)
        {
            var batch = await query.Skip(offset).Take(batchSize)
                .Select(p => new PhotoItem(
                    p.Id, p.FileName, p.FileFormat, p.Width, p.Height, p.Orientation,
                    p.TakenAt, p.Latitude, p.Longitude, p.FileSize
                ))
                .ToListAsync();

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
                        groups.Add(new TimelineGroup(FormatDayLabel(currentDay), currentDay, new List<PhotoItem>(dayPhotos)));
                        dayPhotos.Clear();
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
            groups.Add(new TimelineGroup(FormatDayLabel(currentDay), currentDay, dayPhotos));

        var nextCursor = groups.Count > 0 ? groups[^1].Cursor : null;

        return Ok(new TimelineResponse(groups, nextCursor, groups.Count >= limit));
    }

    private async Task<IActionResult> GetMonthGroups(IOrderedQueryable<Core.Entities.Photo> query, string? cursor, int limit)
    {
        var allPhotos = await query
            .Select(p => new PhotoItem(
                p.Id, p.FileName, p.FileFormat, p.Width, p.Height, p.Orientation,
                p.TakenAt, p.Latitude, p.Longitude, p.FileSize
            ))
            .ToListAsync();

        var grouped = allPhotos
            .GroupBy(p => p.TakenAt?.ToString("yyyy-MM") ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new TimelineGroup(
                FormatMonthLabel(g.Key),
                g.Key,
                g.Take(300).ToList()
            ))
            .ToList();

        // Apply cursor
        var startIdx = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            var idx = grouped.FindIndex(g => g.Cursor == cursor);
            if (idx >= 0) startIdx = idx + 1;
        }

        var result = grouped.Skip(startIdx).Take(limit).ToList();
        var nextCursor = result.Count > 0 ? result[^1].Cursor : null;

        return Ok(new TimelineResponse(result, nextCursor, startIdx + limit < grouped.Count));
    }

    private async Task<IActionResult> GetFlatList(IOrderedQueryable<Core.Entities.Photo> query, string? cursor, int limit)
    {
        int offsetNum = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var c)) offsetNum = c;

        var photos = await query.Skip(offsetNum).Take(limit)
            .Select(p => new PhotoItem(
                p.Id, p.FileName, p.FileFormat, p.Width, p.Height, p.Orientation,
                p.TakenAt, p.Latitude, p.Longitude, p.FileSize
            ))
            .ToListAsync();

        var hasMore = photos.Count >= limit;
        var groups = new List<TimelineGroup> { new(null, null, photos) };

        return Ok(new TimelineResponse(groups, hasMore ? (offsetNum + limit).ToString() : null, hasMore));
    }

    [HttpGet("range")]
    public async Task<IActionResult> GetRange(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string groupLevel = "day",
        [FromQuery] int limit = 200)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var baseQuery = _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt != null
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted));

        DateTime? fromDate = null, toDate = null;
        if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd)) fromDate = fd;
        if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td)) toDate = td;

        if (fromDate.HasValue) baseQuery = baseQuery.Where(p => p.TakenAt >= fromDate.Value);
        if (toDate.HasValue) baseQuery = baseQuery.Where(p => p.TakenAt <= toDate.Value);

        var query = baseQuery.OrderByDescending(p => p.TakenAt);

        return groupLevel switch
        {
            "day" => await GetDayGroups(query, null, limit),
            "month" => await GetMonthGroups(query, null, limit),
            _ => await GetFlatList(query, null, limit),
        };
    }

    [HttpGet("daily-density")]
    public async Task<IActionResult> GetDailyDensity(
        [FromQuery] string direction = "asc")
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var raw = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt != null)
            .GroupBy(p => new { p.TakenAt!.Value.Year, p.TakenAt!.Value.Month, p.TakenAt!.Value.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, count = g.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToListAsync();

        var daily = raw.Select(x => new DailyDensityItem(
            $"{x.Year}-{x.Month:D2}-{x.Day:D2}", x.count
        )).ToList();

        if (direction == "desc")
            daily.Reverse();

        return Ok(daily);
    }

    [HttpGet("date-ids")]
    public async Task<IActionResult> GetDateIds([FromQuery] string date)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        if (!DateTime.TryParse(date, out var dt))
            return BadRequest(new ErrorResult("Invalid date"));

        var start = dt.Date;
        var end = start.AddDays(1);

        var items = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && p.TakenAt >= start && p.TakenAt < end
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted))
            .OrderByDescending(p => p.TakenAt)
            .Select(p => new { p.Id, p.TakenAt })
            .ToListAsync();

        var result = items.Select(item => new DateIdItem(
            item.Id,
            item.TakenAt?.Year is int y
                ? y * 10000 + (item.TakenAt?.Month ?? 1) * 100 + (item.TakenAt?.Day ?? 1)
                : null
        )).ToList();

        return Ok(new DateIdsResponse(date, result));
    }

    [HttpGet("null-date-ids")]
    public async Task<IActionResult> GetNullDateIds()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var items = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt == null
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.Id)
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("years")]
    public async Task<IActionResult> GetYears()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var raw = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt != null)
            .GroupBy(p => p.TakenAt!.Value.Year)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Year)
            .ToListAsync();

        var years = raw.Select(x => new YearCountItem(x.Year, x.Count)).ToList();
        return Ok(years);
    }

    [HttpGet("density")]
    public async Task<IActionResult> GetDensity()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var raw = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted && p.TakenAt != null)
            .GroupBy(p => new { p.TakenAt!.Value.Year, p.TakenAt!.Value.Month })
            .Select(g => new { year = g.Key.Year, month = g.Key.Month, count = g.Count() })
            .OrderByDescending(x => x.year).ThenByDescending(x => x.month)
            .ToListAsync();

        var result = raw
            .GroupBy(d => d.year)
            .Select(g => new MonthlyDensityItem(
                g.Key,
                Enumerable.Range(1, 12).Select(m =>
                    g.FirstOrDefault(x => x.month == m)?.count ?? 0
                ).ToArray()
            ))
            .ToList();

        return Ok(result);
    }

    private static string FormatDayLabel(string d)
        => DateTime.TryParse(d, out var dt) ? $"{dt.Year}年{dt.Month}月{dt.Day}日" : d;

    private static string FormatMonthLabel(string m)
        => DateTime.TryParse(m + "-01", out var dt) ? $"{dt.Year}年{dt.Month}月" : m;
}
