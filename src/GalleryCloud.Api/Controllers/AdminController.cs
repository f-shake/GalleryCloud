using System.ComponentModel;
using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Middleware;
using GalleryCloud.Api.Services;
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
    private readonly IAuthService _authService;
    private readonly UserContext _userContext;
    private readonly FileWatcherService _fileWatcher;

    public AdminController(AppDbContext db, ThumbnailDbContext thumbDb, IScanService scanService,
        ISettingService settingService, IThumbnailService thumbnailService, IAuthService authService,
        UserContext userContext, FileWatcherService fileWatcher)
    {
        _db = db;
        _thumbDb = thumbDb;
        _scanService = scanService;
        _settingService = settingService;
        _thumbnailService = thumbnailService;
        _authService = authService;
        _userContext = userContext;
        _fileWatcher = fileWatcher;
    }

    // ==================== Users ====================

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var users = await _db.Users
            .Select(u => new UserListItem(
                u.Id, u.Username, u.DisplayName,
                !u.IsDeleted, u.CreatedAt,
                u.UserRoots.Where(r => !r.IsDeleted && r.IsEnabled)
                    .Select(r => new UserRootDto(r.Id, r.RootPath, r.IsEnabled, r.CreatedAt))
                    .ToList()
            ))
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new ErrorResult("Username and password are required"));

        if (request.RootPaths == null || request.RootPaths.Count == 0)
            return BadRequest(new ErrorResult("At least one root path is required"));

        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            return Conflict(new ErrorResult("Username already exists"));

        // Validate nesting
        if (HasNesting(request.RootPaths))
            return BadRequest(new ErrorResult("Root paths cannot be nested within each other"));

        var user = new User
        {
            Username = request.Username,
            PasswordHash = _authService.HashPassword(request.Password),
            DisplayName = request.DisplayName,
        };

        foreach (var path in request.RootPaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                user.UserRoots.Add(new UserRoot
                {
                    UserId = user.Id,
                    RootPath = path.Trim()
                });
            }
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Start file watchers for new roots
        foreach (var root in user.UserRoots.Where(r => !r.IsDeleted && r.IsEnabled))
        {
            _fileWatcher.WatchRoot(root);
        }

        var roots = user.UserRoots
            .Where(r => !r.IsDeleted && r.IsEnabled)
            .Select(r => new UserRootDto(r.Id, r.RootPath, r.IsEnabled, r.CreatedAt))
            .ToList();

        return Ok(new UserListItem(
            user.Id, user.Username, user.DisplayName,
            true, user.CreatedAt, roots
        ));
    }

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Password))
            user.PasswordHash = _authService.HashPassword(request.Password);
        if (request.DisplayName != null)
            user.DisplayName = request.DisplayName;
        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value)
            {
                user.IsDeleted = false;
                user.DeletedAt = null;
            }
            else
            {
                user.IsDeleted = true;
                user.DeletedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new MessageResult("Updated"));
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DisableUser(string id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new MessageResult("User disabled"));
    }

    // ==================== User Roots ====================

    [HttpGet("users/{userId}/roots")]
    public async Task<IActionResult> ListUserRoots(string userId)
    {
        var roots = await _db.UserRoots
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .Select(r => new UserRootDto(r.Id, r.RootPath, r.IsEnabled, r.CreatedAt))
            .ToListAsync();
        return Ok(roots);
    }

    [HttpPost("users/{userId}/roots")]
    public async Task<IActionResult> AddUserRoot(string userId, [FromBody] CreateUserRootRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
            return BadRequest(new ErrorResult("RootPath is required"));

        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        // Non-nesting validation
        var existingRoots = await _db.UserRoots
            .Where(r => r.UserId == userId && !r.IsDeleted && r.IsEnabled)
            .Select(r => r.RootPath)
            .ToListAsync();
        var allRoots = existingRoots.Append(request.RootPath.Trim()).ToList();
        if (HasNesting(allRoots))
            return BadRequest(new ErrorResult("Root path cannot be nested within another root"));

        var root = new UserRoot
        {
            UserId = userId,
            RootPath = request.RootPath.Trim()
        };
        _db.UserRoots.Add(root);
        await _db.SaveChangesAsync();

        // Start watcher for this root
        _fileWatcher.WatchRoot(root);

        return Ok(new UserRootDto(root.Id, root.RootPath, root.IsEnabled, root.CreatedAt));
    }

    [HttpDelete("users/{userId}/roots/{rootId}")]
    public async Task<IActionResult> DeleteUserRoot(string userId, string rootId)
    {
        var root = await _db.UserRoots.FirstOrDefaultAsync(r => r.Id == rootId && r.UserId == userId);
        if (root == null) return NotFound();

        // Soft-delete the root
        root.IsDeleted = true;
        root.DeletedAt = DateTime.UtcNow;

        // Soft-delete all photos in this root
        var photos = await _db.Photos.Where(p => p.RootId == rootId).ToListAsync();
        foreach (var photo in photos)
        {
            photo.IsDeleted = true;
            photo.DeletedAt = DateTime.UtcNow;
            photo.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        // Stop watcher
        _fileWatcher.UnwatchRoot(userId, rootId);

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
        {
            await _settingService.SetAsync(key, value);
        }

        return Ok(new MessageResult("Settings updated"));
    }

    // ==================== Scan ====================

    [HttpPost("scan/trigger")]
    public async Task<IActionResult> TriggerScan()
    {
        if (_scanService.Status.IsRunning)
            return Conflict(new ErrorResult("Scan is already running"));

        _ = Task.Run(() => _scanService.TriggerFullScanForAllUsersAsync());
        return Ok(new MessageResult("Scan started"));
    }

    [HttpPost("scan/trigger-incremental")]
    public async Task<IActionResult> TriggerIncrementalScan()
    {
        if (_scanService.Status.IsRunning)
            return Conflict(new ErrorResult("Scan is already running"));

        _ = Task.Run(() => _scanService.TriggerFullScanForAllUsersAsync());
        return Ok(new MessageResult("Incremental scan started"));
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
        return Ok(_scanService.Status);
    }

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
    public async Task<IActionResult> GetThumbnailStats()
    {
        var stats = await _thumbnailService.GetStatsAsync();
        return Ok(stats);
    }

    [HttpPost("thumbnails/regenerate")]
    public IActionResult RegenerateThumbnails([FromBody] ThumbnailGenerationRequest? request)
    {
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("Thumbnail regeneration is already running"));

        _ = Task.Run(() => _thumbnailService.RegenerateAllAsync(request?.Sizes));
        return Ok(new MessageResult("Thumbnail regeneration started"));
    }

    [HttpPost("thumbnails/fill-missing")]
    public IActionResult FillMissingThumbnails([FromBody] ThumbnailGenerationRequest? request)
    {
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("Thumbnail generation is already running"));

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
    public IActionResult GetThumbnailStatus()
    {
        return Ok(_thumbnailService.RegenerationStatus);
    }

    [HttpDelete("thumbnails/cache")]
    public async Task<IActionResult> ClearThumbnailCache()
    {
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new ErrorResult("Thumbnail regeneration is running, wait for it to finish"));

        // Clear DB records
        var count = await _thumbDb.ThumbnailCaches.CountAsync();
        _thumbDb.ThumbnailCaches.RemoveRange(_thumbDb.ThumbnailCaches);
        await _thumbDb.SaveChangesAsync();

        return Ok(new MessageResult("Cache cleared")); // count was sent but not used by frontend
    }

    // ==================== Stats ====================

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalPhotos = await _db.Photos.CountAsync(p => !p.IsDeleted);
        var totalUsers = await _db.Users.CountAsync();
        var totalSize = await _db.Photos.Where(p => !p.IsDeleted).SumAsync(p => p.FileSize);
        var photosWithGps = await _db.Photos.CountAsync(p => !p.IsDeleted && p.Latitude != null);
        var formatDistribution = await _db.Photos
            .Where(p => !p.IsDeleted)
            .GroupBy(p => p.FileFormat)
            .Select(g => new FormatCountItem(g.Key, g.Count()))
            .ToListAsync();

        return Ok(new AdminStats(
            totalPhotos, totalUsers, totalSize,
            Math.Round(totalSize / (1024.0 * 1024 * 1024), 2),
            photosWithGps, formatDistribution
        ));
    }

    // ==================== Helpers ====================

    private static bool HasNesting(List<string> roots)
    {
        var normalized = roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.GetFullPath(r.Trim()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToList();

        for (int i = 0; i < normalized.Count; i++)
        {
            for (int j = 0; j < normalized.Count; j++)
            {
                if (i != j && normalized[j].StartsWith(normalized[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
