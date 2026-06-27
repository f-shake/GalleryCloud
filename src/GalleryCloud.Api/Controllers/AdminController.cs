using GalleryCloud.Api.Data;
using GalleryCloud.Api.Middleware;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/admin")]
[AdminOnly]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IScanService _scanService;
    private readonly ISettingService _settingService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IAuthService _authService;
    private readonly UserContext _userContext;

    public AdminController(AppDbContext db, IScanService scanService, ISettingService settingService,
        IThumbnailService thumbnailService, IAuthService authService, UserContext userContext)
    {
        _db = db;
        _scanService = scanService;
        _settingService = settingService;
        _thumbnailService = thumbnailService;
        _authService = authService;
        _userContext = userContext;
    }

    // ==================== Users ====================

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var users = await _db.Users
            .Select(u => new
            {
                u.Id, u.Username, u.DisplayName, u.RootPath,
                u.IsAdmin, u.IsActive, u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    public record CreateUserRequest(string Username, string Password, string? DisplayName, string RootPath, bool IsAdmin);

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password are required" });

        if (string.IsNullOrWhiteSpace(request.RootPath))
            return BadRequest(new { error = "RootPath is required" });

        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            return Conflict(new { error = "Username already exists" });

        var user = new User
        {
            Username = request.Username,
            PasswordHash = _authService.HashPassword(request.Password),
            DisplayName = request.DisplayName,
            RootPath = request.RootPath,
            IsAdmin = request.IsAdmin,
            IsActive = true,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new { user.Id, user.Username, user.DisplayName, user.RootPath, user.IsAdmin });
    }

    public record UpdateUserRequest(string? Password, string? DisplayName, string? RootPath, bool? IsAdmin, bool? IsActive);

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Password))
            user.PasswordHash = _authService.HashPassword(request.Password);
        if (request.DisplayName != null)
            user.DisplayName = request.DisplayName;
        if (request.RootPath != null)
            user.RootPath = request.RootPath;
        if (request.IsAdmin.HasValue)
            user.IsAdmin = request.IsAdmin.Value;
        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Updated" });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DisableUser(string id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "User disabled" });
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

        return Ok(new { message = "Settings updated" });
    }

    // ==================== Scan ====================

    [HttpPost("scan/trigger")]
    public async Task<IActionResult> TriggerScan()
    {
        if (_scanService.Status.IsRunning)
            return Conflict(new { error = "Scan is already running" });

        _ = Task.Run(() => _scanService.TriggerFullScanForAllUsersAsync());
        return Ok(new { message = "Scan started" });
    }

    [HttpPost("scan/trigger-incremental")]
    public async Task<IActionResult> TriggerIncrementalScan()
    {
        if (_scanService.Status.IsRunning)
            return Conflict(new { error = "Scan is already running" });

        _ = Task.Run(() => _scanService.TriggerFullScanForAllUsersAsync());
        return Ok(new { message = "Incremental scan started" });
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
            .Select(l => new
            {
                l.Id, l.UserId, l.StartedAt, l.FinishedAt,
                l.TotalFound, l.NewAdded, l.SoftDeleted, l.Mode
            })
            .ToListAsync();

        return Ok(logs);
    }

    // ==================== Thumbnails ====================

    [HttpPost("thumbnails/regenerate")]
    public IActionResult RegenerateThumbnails()
    {
        if (_thumbnailService.RegenerationStatus.IsRunning)
            return Conflict(new { error = "Thumbnail regeneration is already running" });

        _ = Task.Run(() => _thumbnailService.RegenerateAllAsync());
        return Ok(new { message = "Thumbnail regeneration started" });
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
            return Conflict(new { error = "Thumbnail regeneration is running, wait for it to finish" });

        // Clear DB records
        var count = await _db.ThumbnailCaches.CountAsync();
        _db.ThumbnailCaches.RemoveRange(_db.ThumbnailCaches);
        await _db.SaveChangesAsync();

        // Clear disk cache
        var cacheDir = await _settingService.GetAsync("thumbnail.cacheDir", "data/thumbnails");
        var path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), cacheDir));
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }

        return Ok(new { message = "Cache cleared", deletedRecords = count });
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
            .Select(g => new { format = g.Key, count = g.Count() })
            .ToListAsync();

        return Ok(new
        {
            totalPhotos,
            totalUsers,
            totalSize,
            totalSizeGb = Math.Round(totalSize / (1024.0 * 1024 * 1024), 2),
            photosWithGps,
            formatDistribution
        });
    }
}
