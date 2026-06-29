using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/tags")]
public class TagsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;

    public TagsController(AppDbContext db, UserContext userContext)
    {
        _db = db;
        _userContext = userContext;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        var tags = await _db.Tags
            .Where(t => t.UserId == _userContext.UserId)
            .Select(t => new TagItem(t.Id, t.Name, t.Color))
            .ToListAsync();
        return Ok(tags);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTagRequest req)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        var tag = new Tag { UserId = _userContext.UserId, Name = req.Name, Color = req.Color };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        return Ok(new TagItem(tag.Id, tag.Name, tag.Color));
    }

    [HttpPost("photos/{photoId}")]
    public async Task<IActionResult> AddToPhoto(string photoId, [FromBody] CreateTagRequest req)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.UserId == _userContext.UserId && t.Name == req.Name)
                  ?? new Tag { UserId = _userContext.UserId, Name = req.Name, Color = req.Color };

        if (string.IsNullOrEmpty(tag.Id))
            _db.Tags.Add(tag);

        var exists = await _db.PhotoTags.AnyAsync(pt => pt.PhotoId == photoId && pt.TagId == tag.Id);
        if (!exists)
        {
            _db.PhotoTags.Add(new PhotoTag { PhotoId = photoId, TagId = tag.Id });
            await _db.SaveChangesAsync();
        }

        return Ok(new TagItem(tag.Id, tag.Name, tag.Color));
    }

    [HttpDelete("photos/{photoId}/{tagId}")]
    public async Task<IActionResult> RemoveFromPhoto(string photoId, string tagId)
    {
        var pt = await _db.PhotoTags.FindAsync(photoId, tagId);
        if (pt != null)
        {
            _db.PhotoTags.Remove(pt);
            await _db.SaveChangesAsync();
        }
        return Ok();
    }
}
