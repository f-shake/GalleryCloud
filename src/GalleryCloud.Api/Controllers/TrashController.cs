using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/trash")]
public class TrashController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;

    public TrashController(AppDbContext db, UserContext userContext)
    {
        _db = db;
        _userContext = userContext;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized();

        var query = _db.Photos
            .IgnoreQueryFilters()
            .Where(p => p.UserId == _userContext.UserId && p.IsDeleted)
            .OrderByDescending(p => p.DeletedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(p => new TrashItem(
                p.Id, p.FileName, p.FileFormat,
                p.Width, p.Height, p.Orientation,
                p.TakenAt,
                p.DeletedAt ?? DateTime.MinValue,
                p.FileSize
            ))
            .ToListAsync();

        return Ok(new TrashListResponse(total, items));
    }
}
