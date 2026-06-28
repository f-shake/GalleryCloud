using GalleryCloud.Api.Data;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Enums;
using GalleryCloud.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/thumbnails")]
public class ThumbnailReadyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IThumbnailService _thumbnailService;
    private readonly UserContext _userContext;

    public ThumbnailReadyController(AppDbContext db, IThumbnailService thumbnailService, UserContext userContext)
    {
        _db = db;
        _thumbnailService = thumbnailService;
        _userContext = userContext;
    }

    public record IdsRequest(List<string> Ids, string Size = "grid", int Width = 400);

    /// <summary>
    /// POST /api/thumbnails/ready
    /// Given a list of photo IDs, returns which ones have cached thumbnails ready.
    /// Pure read — no writes.
    /// </summary>
    [HttpPost("ready")]
    public async Task<IActionResult> CheckReady([FromBody] IdsRequest request)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        if (request.Ids == null || request.Ids.Count == 0)
            return Ok(new { ready = new List<string>(), pending = new List<string>() });

        var size = request.Size.ToLowerInvariant();

        var cachedIds = await _db.ThumbnailCaches
            .Where(t => t.Size == size && request.Ids.Contains(t.PhotoId))
            .Select(t => t.PhotoId)
            .ToListAsync();

        var readySet = new HashSet<string>(cachedIds);
        var ready = request.Ids.Where(id => readySet.Contains(id)).ToList();
        var pending = request.Ids.Where(id => !readySet.Contains(id)).ToList();

        return Ok(new { ready, pending });
    }

    /// <summary>
    /// POST /api/thumbnails/enqueue
    /// Enqueue a batch of photos for thumbnail generation. Returns immediately.
    /// </summary>
    [HttpPost("enqueue")]
    public async Task<IActionResult> Enqueue([FromBody] IdsRequest request)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var size = request.Size?.ToLowerInvariant() switch
        {
            "preview" => ThumbnailSize.Preview,
            _ => ThumbnailSize.Grid
        };

        var sizeKey = request.Size?.ToLowerInvariant() ?? "grid";
        var alreadyCached = await _db.ThumbnailCaches
            .Where(t => t.Size == sizeKey && request.Ids!.Contains(t.PhotoId))
            .Select(t => t.PhotoId)
            .ToListAsync();

        var cachedSet = new HashSet<string>(alreadyCached);
        var enqueued = 0;

        if (request.Ids != null)
        {
            foreach (var id in request.Ids)
            {
                if (cachedSet.Contains(id)) continue;
                _thumbnailService.EnqueueAsync(id, size, request.Width);
                enqueued++;
            }
        }

        return Ok(new { enqueued });
    }
}
