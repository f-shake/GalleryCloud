using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Helpers;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Enums;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
public class ShareController : ControllerBase
{
    private readonly ShareService _shareService;
    private readonly UserContext _userContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IThumbnailService? _thumbnailService;
    private readonly ISettingService _settingService;

    public ShareController(ShareService shareService, UserContext userContext,
        IServiceScopeFactory scopeFactory, ISettingService settingService,
        IThumbnailService? thumbnailService = null)
    {
        _shareService = shareService;
        _userContext = userContext;
        _scopeFactory = scopeFactory;
        _settingService = settingService;
        _thumbnailService = thumbnailService;
    }

    // ====== Authenticated endpoints ======

    [HttpGet("api/shares")]
    public async Task<IActionResult> List()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        var shares = await _shareService.ListSharesAsync(_userContext.UserId!);
        return Ok(shares);
    }

    [HttpPost("api/shares")]
    public async Task<IActionResult> Create([FromBody] CreateShareRequest request)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        var expireDays = request.ExpireDays ?? 30;
        if (expireDays < 1 && expireDays != 0)
            return BadRequest(new ErrorResult("ExpireDays must be at least 1, or 0 for permanent"));
        if (expireDays > 365)
            return BadRequest(new ErrorResult("ExpireDays cannot exceed 365"));

        var result = await _shareService.CreateShareAsync(_userContext.UserId!, request.Name,
            expireDays == 0 ? null : expireDays, request.AllowDownload, request.AllowMetadata);
        return Ok(result);
    }

    [HttpGet("api/shares/{shareId}")]
    public async Task<IActionResult> Get(string shareId)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        try
        {
            var result = await _shareService.GetShareAsync(shareId, _userContext.UserId!);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPatch("api/shares/{shareId}")]
    public async Task<IActionResult> Extend(string shareId, [FromBody] ExtendShareRequest request)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        try
        {
            await _shareService.ExtendShareAsync(shareId, _userContext.UserId!, request.Name, request.ExpireDays);
            return Ok(new MessageResult("Updated"));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("api/shares/{shareId}")]
    public async Task<IActionResult> Delete(string shareId)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        try
        {
            await _shareService.DeleteShareAsync(shareId, _userContext.UserId!);
            return Ok(new MessageResult("Deleted"));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("api/shares/{shareId}/photos")]
    public async Task<IActionResult> AddPhotos(string shareId, [FromBody] AddPhotosToShareRequest request)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        try
        {
            var result = await _shareService.AddPhotosToShareAsync(shareId, _userContext.UserId!, request.PhotoIds);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("api/shares/{shareId}/photos/{photoId}")]
    public async Task<IActionResult> RemovePhoto(string shareId, string photoId)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        try
        {
            await _shareService.RemovePhotoFromShareAsync(shareId, _userContext.UserId!, photoId);
            return Ok(new MessageResult("Removed"));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ====== Public endpoints (no auth required) ======

    [HttpGet("api/public/shares/{token}")]
    public async Task<IActionResult> GetPublic(string token)
    {
        try
        {
            var result = await _shareService.GetPublicShareAsync(token);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(new ErrorResult("Share not found")); }
        catch (InvalidOperationException ex) { return BadRequest(new ErrorResult(ex.Message)); }
    }

    [HttpGet("api/public/shares/{token}/photos/{photoId}/thumbnail")]
    public async Task<IActionResult> GetPublicThumbnail(string token, string photoId,
        [FromQuery] string size = "grid", [FromQuery] int w = 400)
    {
        // Validate share access
        if (!await IsPhotoInShare(token, photoId))
            return NotFound();

        // Forward to thumbnail service
        if (_thumbnailService == null)
            return StatusCode(500, new ErrorResult("Thumbnail service not available"));

        var thumbSize = size switch
        {
            "grid" => ThumbnailSize.Grid,
            "preview" => ThumbnailSize.Preview,
            _ => ThumbnailSize.Grid
        };

        // 即时生成（浏览器 <img> 无法处理 202，必须同步返回图片）
        var result = await _thumbnailService.GetThumbnailAsync(photoId, thumbSize, w, HttpContext.RequestAborted);
        if (result == null) return NotFound();
        var fmt = await _settingService.GetAsync(
            size == "preview" ? SettingKeys.PreviewFormat : SettingKeys.ThumbnailFormat, "jpeg");
        var contentType = fmt.Equals("webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "image/jpeg";
        return new FileContentResult(await StreamHelper.ReadFullyAsync(result), contentType);
    }

    [HttpGet("api/public/shares/{token}/photos/{photoId}/file")]
    public async Task<IActionResult> GetPublicFile(string token, string photoId)
    {
        // Validate share access and download permission
        var share = await GetShareByTokenAsync(token);
        if (share == null || !await IsPhotoInShare(token, photoId))
            return NotFound();
        if (!share.AllowDownload)
            return Forbid();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var photo = await db.Photos
            .Include(p => p.Root)
            .FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted);

        if (photo?.Root == null)
            return NotFound();

        var fullPath = Path.GetFullPath(Path.Combine(photo.Root.RootPath, photo.FilePath));
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new ErrorResult("File not found on disk"));

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

    private async Task<bool> IsPhotoInShare(string token, string photoId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Shares
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Token == token && !s.IsDeleted
                && (!s.ExpiresAt.HasValue || s.ExpiresAt > DateTime.UtcNow)
                && s.SharePhotos.Any(sp => sp.PhotoId == photoId && !sp.IsDeleted));
    }

    private async Task<Share?> GetShareByTokenAsync(string token)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Shares
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Token == token && !s.IsDeleted
                && (!s.ExpiresAt.HasValue || s.ExpiresAt > DateTime.UtcNow));
    }
}
