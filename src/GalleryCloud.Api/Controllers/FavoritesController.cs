using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/favorites")]
public class FavoritesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;

    public FavoritesController(AppDbContext db, UserContext userContext)
    {
        _db = db;
        _userContext = userContext;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var photoIds = await _db.Favorites
            .Where(f => f.UserId == _userContext.UserId)
            .OrderByDescending(f => f.AddedAt)
            .Select(f => f.PhotoId)
            .ToListAsync();

        var total = photoIds.Count;
        var pageIds = photoIds.Skip((page - 1) * limit).Take(limit).ToList();

        var photos = await _db.Photos
            .Where(p => pageIds.Contains(p.Id))
            .Select(p => new PhotoItem(
                p.Id, p.FileName, p.FileFormat,
                p.Width, p.Height, p.Orientation,
                p.TakenAt, p.Latitude, p.Longitude, p.FileSize
            ))
            .ToListAsync();

        // Preserve order
        var photoMap = photos.ToDictionary(p => p.Id);
        var ordered = pageIds.Where(id => photoMap.ContainsKey(id)).Select(id => photoMap[id]).ToList();

        return Ok(new PhotoListResponse(total, page, limit, ordered));
    }

    [HttpPost("{photoId}")]
    public async Task<IActionResult> Add(string photoId)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        if (!await _db.Favorites.AnyAsync(f => f.UserId == _userContext.UserId && f.PhotoId == photoId))
        {
            _db.Favorites.Add(new Favorite { UserId = _userContext.UserId, PhotoId = photoId });
            await _db.SaveChangesAsync();
        }

        return Ok(new FavoriteResult(true));
    }

    [HttpDelete("{photoId}")]
    public async Task<IActionResult> Remove(string photoId)
    {
        var fav = await _db.Favorites
            .FirstOrDefaultAsync(f => f.UserId == _userContext.UserId && f.PhotoId == photoId);
        if (fav != null)
        {
            _db.Favorites.Remove(fav);
            await _db.SaveChangesAsync();
        }

        return Ok(new FavoriteResult(false));
    }
}
