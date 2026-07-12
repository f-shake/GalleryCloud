using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Dtos;
using GalleryCloud.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/user")]
public class UserPanelController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;
    private readonly IScanService _scanService;
    private readonly IAuthService _authService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IStatsService _statsService;
    private readonly ILogger<UserPanelController> _logger;

    public UserPanelController(AppDbContext db, UserContext userContext, IScanService scanService,
        IAuthService authService, IThumbnailService thumbnailService, IStatsService statsService,
        ILogger<UserPanelController> logger)
    {
        _db = db;
        _userContext = userContext;
        _scanService = scanService;
        _authService = authService;
        _thumbnailService = thumbnailService;
        _statsService = statsService;
        _logger = logger;
    }

    // ==================== Scan ====================

    [HttpPost("scan/trigger")]
    public async Task<IActionResult> TriggerScan()
    {
        if (!_userContext.IsAuthenticated || _userContext.IsAdmin)
            return Unauthorized();

        if (_scanService.Status.IsRunning)
            return Conflict(new ErrorResult("当前有扫描任务正在运行，请稍后再试"));
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("当前有缩略图生成任务正在运行，无法同时扫描，请稍后再试"));

        _ = Task.Run(async () =>
        {
            try { await _scanService.TriggerFullScanAsync(_userContext.UserId); }
            catch (Exception ex) { _logger.LogError(ex, "Full scan failed"); }
        });
        return Ok(new MessageResult("Scan started"));
    }

    [HttpPost("scan/cancel")]
    public IActionResult CancelScan()
    {
        _scanService.Cancel();
        return Ok(new MessageResult("Cancelling..."));
    }

    [HttpGet("scan/status")]
    public IActionResult GetScanStatus()
    {
        var s = _scanService.Status;
        // Strip userId to avoid leaking other users
        return Ok(new ScanStatus
        {
            IsRunning = s.IsRunning,
            Mode = s.Mode,
            StartedAt = s.StartedAt,
            ProcessedFiles = s.ProcessedFiles,
            TotalFiles = s.TotalFiles
        });
    }

    [HttpPost("scan/refresh-exif")]
    public async Task<IActionResult> RefreshExif()
    {
        if (!_userContext.IsAuthenticated || _userContext.IsAdmin)
            return Unauthorized();

        if (_scanService.Status.IsRunning)
            return Conflict(new ErrorResult("当前有扫描任务正在运行，请稍后再试"));
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("当前有缩略图生成任务正在运行，无法刷新EXIF，请稍后再试"));

        _ = Task.Run(async () =>
        {
            try { await _scanService.RefreshExifAsync(_userContext.UserId); }
            catch (Exception ex) { _logger.LogError(ex, "EXIF refresh failed"); }
        });
        return Ok(new MessageResult("EXIF refresh started"));
    }

    [HttpGet("scan/logs")]
    public async Task<IActionResult> GetScanLogs([FromQuery] int limit = 50)
    {
        if (!_userContext.IsAuthenticated || _userContext.IsAdmin)
            return Unauthorized();

        var logs = await _db.ScanLogs
            .Where(l => l.UserId == _userContext.UserId)
            .OrderByDescending(l => l.StartedAt)
            .Take(limit)
            .Select(l => new ScanLogItem(
                l.Id, l.UserId, l.StartedAt, l.FinishedAt,
                l.TotalFound, l.NewAdded, l.SoftDeleted, l.Mode
            ))
            .ToListAsync();

        return Ok(logs);
    }

    // ==================== Password ====================

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!_userContext.IsAuthenticated || _userContext.IsAdmin)
            return Unauthorized();

        var user = await _db.Users.FindAsync(_userContext.UserId);
        if (user == null) return NotFound();

        if (!_authService.VerifyPassword(request.OldPassword, user.PasswordHash))
            return BadRequest(new ErrorResult("当前密码不正确"));

        user.PasswordHash = _authService.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();

        return Ok(new MessageResult("密码已修改"));
    }

    // ==================== Thumbnails ====================

    [HttpGet("thumbnails/stats")]
    public async Task<IActionResult> GetThumbnailStats()
    {
        var stats = await _thumbnailService.GetStatsAsync();
        return Ok(stats);
    }

    [HttpPost("thumbnails/fill-missing")]
    public IActionResult FillMissingThumbnails([FromBody] ThumbnailGenerationRequest? request)
    {
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("当前有缩略图生成任务正在运行，请稍后再试"));
        if (_scanService.Status.IsRunning)
            return Conflict(new ErrorResult("当前有扫描任务正在运行，无法生成缩略图，请稍后再试"));

        _ = Task.Run(() => _thumbnailService.FillMissingAsync(request?.Sizes));
        return Ok(new MessageResult("Fill-missing started"));
    }

    [HttpPost("thumbnails/regenerate")]
    public IActionResult RegenerateThumbnails([FromBody] ThumbnailGenerationRequest? request)
    {
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("当前有缩略图生成任务正在运行，请稍后再试"));
        if (_scanService.Status.IsRunning)
            return Conflict(new ErrorResult("当前有扫描任务正在运行，无法生成缩略图，请稍后再试"));

        _ = Task.Run(() => _thumbnailService.RegenerateAllAsync(request?.Sizes));
        return Ok(new MessageResult("Thumbnail regeneration started"));
    }

    [HttpPost("thumbnails/cancel")]
    public IActionResult CancelGeneration()
    {
        _thumbnailService.CancelGeneration();
        return Ok(new MessageResult("Cancelling..."));
    }

    [HttpGet("thumbnails/status")]
    public IActionResult GetThumbnailStatus()
    {
        return Ok(_thumbnailService.RegenerationStatus);
    }

    [HttpDelete("thumbnails/cache")]
    public async Task<IActionResult> ClearThumbnailCache()
    {
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("Thumbnail generation is running, wait for it to finish"));

        var thumbDb = HttpContext.RequestServices.GetRequiredService<ThumbnailDbContext>();
        var count = await thumbDb.ThumbnailCaches.CountAsync();
        thumbDb.ThumbnailCaches.RemoveRange(thumbDb.ThumbnailCaches);
        await thumbDb.SaveChangesAsync();
        _logger.LogInformation("用户 {UserId} 清除了缩略图缓存：{Count} 条记录", _userContext.UserId, count);

        return Ok(new MessageResult("Cache cleared"));
    }

    // ==================== Stats ====================

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        if (!_userContext.IsAuthenticated || _userContext.IsAdmin)
            return Unauthorized();

        return Ok(await _statsService.GetUserStatsAsync(_userContext.UserId!));
    }
}
