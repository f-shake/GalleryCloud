using System.ComponentModel;
using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Middleware;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Dtos;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/admin")]
[AdminOnly]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ThumbnailDbContext _thumbDb;
    private readonly IScanService _scanService;
    private readonly ISettingService _settingService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IUserService _userService;
    private readonly IStatsService _statsService;
    private readonly IFilesystemBrowserService _fsService;
    private readonly FileWatcherService _fileWatcher;
    private readonly ILogger<AdminController> _logger;

    public AdminController(AppDbContext db, ThumbnailDbContext thumbDb, IScanService scanService,
        ISettingService settingService, IThumbnailService thumbnailService, IUserService userService,
        IStatsService statsService, IFilesystemBrowserService fsService, FileWatcherService fileWatcher,
        ILogger<AdminController> logger)
    {
        _db = db;
        _thumbDb = thumbDb;
        _scanService = scanService;
        _settingService = settingService;
        _thumbnailService = thumbnailService;
        _userService = userService;
        _statsService = statsService;
        _fsService = fsService;
        _fileWatcher = fileWatcher;
        _logger = logger;
    }

    // ==================== Users ====================

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers() =>
        Ok(await _userService.ListUsersAsync());

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            return Ok(await _userService.CreateUserAsync(request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResult(ex.Message));
        }
    }

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var user = await _userService.UpdateUserAsync(id, request);
            if (user == null) return NotFound();
            return Ok(new MessageResult("Updated"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResult(ex.Message));
        }
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DisableUser(string id)
    {
        var ok = await _userService.DisableUserAsync(id);
        if (!ok) return NotFound();
        return Ok(new MessageResult("User disabled"));
    }

    // ==================== User Roots ====================

    [HttpGet("users/{userId}/roots")]
    public async Task<IActionResult> ListUserRoots(string userId) =>
        Ok(await _userService.ListRootsAsync(userId));

    [HttpPost("users/{userId}/roots")]
    public async Task<IActionResult> AddUserRoot(string userId, [FromBody] CreateUserRootRequest request)
    {
        try
        {
            var root = await _userService.AddRootAsync(userId, request.RootPath);
            if (root == null) return NotFound();
            return Ok(root);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResult(ex.Message));
        }
    }

    [HttpDelete("users/{userId}/roots/{rootId}")]
    public async Task<IActionResult> DeleteUserRoot(string userId, string rootId)
    {
        var ok = await _userService.DeleteRootAsync(userId, rootId);
        if (!ok) return NotFound();
        return Ok(new MessageResult("Root deleted"));
    }

    // ==================== Settings ====================

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _settingService.GetAllAsync();
        return Ok(settings);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] Dictionary<string, string> updates)
    {
        foreach (var (key, value) in updates)
            await _settingService.SetAsync(key, value);

        // FileWatcher 开关即时生效
        if (updates.TryGetValue(SettingKeys.FileWatcherEnabled, out var enabled))
        {
            if (enabled == "false")
                _fileWatcher.StopAll();
            else if (enabled == "true")
                await _fileWatcher.InitializeAsync();
        }

        return Ok(new MessageResult("Settings updated"));
    }

    // ==================== Scan ====================

    [HttpPost("scan/trigger")]
    public async Task<IActionResult> TriggerScan()
    {
        if (_scanService.Status.IsRunning)
            return Conflict(new ErrorResult("当前有扫描任务正在运行，请稍后再试"));
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("当前有缩略图生成任务正在运行，无法同时扫描，请稍后再试"));

        _ = Task.Run(async () =>
        {
            try { await _scanService.TriggerFullScanForAllUsersAsync(); }
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
    public IActionResult GetScanStatus() => Ok(_scanService.Status);

    [HttpGet("scan/logs")]
    public async Task<IActionResult> GetScanLogs([FromQuery] int limit = 50)
    {
        var logs = await _db.ScanLogs
            .OrderByDescending(l => l.StartedAt)
            .Take(limit)
            .Select(l => new ScanLogItem(
                l.Id, l.UserId, l.StartedAt, l.FinishedAt,
                l.TotalFound, l.NewAdded, l.SoftDeleted, l.Mode
            ))
            .ToListAsync();
        return Ok(logs);
    }

    // ==================== Thumbnails ====================

    [HttpGet("thumbnails/stats")]
    public async Task<IActionResult> GetThumbnailStats() =>
        Ok(await _thumbnailService.GetStatsAsync());

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

    [HttpPost("thumbnails/cancel")]
    public IActionResult CancelGeneration()
    {
        _thumbnailService.CancelGeneration();
        return Ok(new MessageResult("Cancelling..."));
    }

    [HttpGet("thumbnails/status")]
    public IActionResult GetThumbnailStatus() => Ok(_thumbnailService.RegenerationStatus);

    [HttpDelete("thumbnails/cache")]
    public async Task<IActionResult> ClearThumbnailCache()
    {
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("Thumbnail regeneration is running, wait for it to finish"));

        var count = await _thumbDb.ThumbnailCaches.CountAsync();
        _thumbDb.ThumbnailCaches.RemoveRange(_thumbDb.ThumbnailCaches);
        await _thumbDb.SaveChangesAsync();
        return Ok(new MessageResult("Cache cleared"));
    }

    // ==================== Stats ====================

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats() =>
        Ok(await _statsService.GetAdminStatsAsync());

    // ==================== Filesystem Browser ====================

    [HttpGet("fs/drives")]
    public async Task<IActionResult> GetDrives()
    {
        try
        {
            return Ok(await _fsService.GetDrivesAsync());
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ErrorResult($"Failed to enumerate drives: {ex.Message}"));
        }
    }

    [HttpGet("fs/browse")]
    public async Task<IActionResult> BrowseDirectory([FromQuery] string path = "")
    {
        try
        {
            var result = await _fsService.BrowseDirectoryAsync(path);
            if (result == null)
                return BadRequest(new ErrorResult($"Directory not found: {path}"));
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new ErrorResult("Access denied"));
        }
        catch (Exception ex)
        {
            return BadRequest(new ErrorResult(ex.Message));
        }
    }
}
