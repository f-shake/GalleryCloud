using GalleryCloud.Api.Data;
using GalleryCloud.Core.Dtos;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GalleryCloud.Api.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IAuthService _auth;
    private readonly IScanService _scanService;
    private readonly IThumbnailService _thumbnailService;
    private readonly FileWatcherService _fileWatcher;
    private readonly ILogger<UserService> _logger;

    public UserService(AppDbContext db, IAuthService auth, IScanService scanService,
        IThumbnailService thumbnailService, FileWatcherService fileWatcher,
        ILogger<UserService> logger)
    {
        _db = db;
        _auth = auth;
        _scanService = scanService;
        _thumbnailService = thumbnailService;
        _fileWatcher = fileWatcher;
        _logger = logger;
    }

    public async Task<List<UserListItem>> ListUsersAsync()
    {
        return await _db.Users
            .Select(u => new UserListItem(
                u.Id, u.Username, u.DisplayName,
                !u.IsDeleted, u.CreatedAt,
                u.UserRoots.Where(r => !r.IsDeleted && r.IsEnabled)
                    .Select(r => new UserRootDto(r.Id, r.RootPath, r.IsEnabled, r.CreatedAt))
                    .ToList()
            ))
            .ToListAsync();
    }

    public async Task<UserListItem> CreateUserAsync(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("Username and password are required");

        if (request.RootPaths == null || request.RootPaths.Count == 0)
            throw new InvalidOperationException("At least one root path is required");

        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            throw new InvalidOperationException("Username already exists");

        if (HasNesting(request.RootPaths))
            throw new InvalidOperationException("Root paths cannot be nested within each other");

        var user = new User
        {
            Username = request.Username,
            PasswordHash = _auth.HashPassword(request.Password),
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

        foreach (var root in user.UserRoots.Where(r => !r.IsDeleted && r.IsEnabled))
        {
            _fileWatcher?.WatchRoot(root);
        }

        var roots = user.UserRoots
            .Where(r => !r.IsDeleted && r.IsEnabled)
            .Select(r => new UserRootDto(r.Id, r.RootPath, r.IsEnabled, r.CreatedAt))
            .ToList();

        return new UserListItem(user.Id, user.Username, user.DisplayName, true, user.CreatedAt, roots);
    }

    public async Task<UserListItem?> UpdateUserAsync(string id, UpdateUserRequest request)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;

        if (!string.IsNullOrWhiteSpace(request.Password))
            user.PasswordHash = _auth.HashPassword(request.Password);
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

        if (request.RootPaths != null)
        {
            await CancelTasksForUserAsync(id);

            var valid = request.RootPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            if (valid.Count == 0)
                throw new InvalidOperationException("至少需要一个根目录");
            if (HasNesting(valid))
                throw new InvalidOperationException("根目录不能相互嵌套");

            var existing = await _db.UserRoots.Where(r => r.UserId == id && !r.IsDeleted).ToListAsync();
            var existingByPath = existing.ToDictionary(r => r.RootPath, StringComparer.OrdinalIgnoreCase);

            foreach (var root in existing)
            {
                if (!valid.Contains(root.RootPath, StringComparer.OrdinalIgnoreCase))
                {
                    root.IsDeleted = true;
                    root.DeletedAt = DateTime.UtcNow;
                    _fileWatcher?.UnwatchRoot(id, root.Id);
                }
            }

            foreach (var path in valid)
            {
                if (!existingByPath.ContainsKey(path))
                {
                    var root = new UserRoot { UserId = id, RootPath = path };
                    _db.UserRoots.Add(root);
                    _fileWatcher?.WatchRoot(root);
                }
            }
        }

        await _db.SaveChangesAsync();

        var roots = await _db.UserRoots
            .Where(r => r.UserId == id && !r.IsDeleted && r.IsEnabled)
            .Select(r => new UserRootDto(r.Id, r.RootPath, r.IsEnabled, r.CreatedAt))
            .ToListAsync();

        return new UserListItem(id, user.Username, user.DisplayName, !user.IsDeleted, user.CreatedAt, roots);
    }

    public async Task<bool> DisableUserAsync(string id)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return false;

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<UserRootDto>> ListRootsAsync(string userId)
    {
        return await _db.UserRoots
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .Select(r => new UserRootDto(r.Id, r.RootPath, r.IsEnabled, r.CreatedAt))
            .ToListAsync();
    }

    public async Task<UserRootDto?> AddRootAsync(string userId, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new InvalidOperationException("RootPath is required");

        await CancelTasksForUserAsync(userId);

        var user = await _db.Users.FindAsync(userId);
        if (user == null) return null;

        var existingRoots = await _db.UserRoots
            .Where(r => r.UserId == userId && !r.IsDeleted && r.IsEnabled)
            .Select(r => r.RootPath)
            .ToListAsync();
        var allRoots = existingRoots.Append(rootPath.Trim()).ToList();
        if (HasNesting(allRoots))
            throw new InvalidOperationException("Root path cannot be nested within another root");

        var root = new UserRoot
        {
            UserId = userId,
            RootPath = rootPath.Trim()
        };
        _db.UserRoots.Add(root);
        await _db.SaveChangesAsync();

        _fileWatcher?.WatchRoot(root);

        return new UserRootDto(root.Id, root.RootPath, root.IsEnabled, root.CreatedAt);
    }

    public async Task<bool> DeleteRootAsync(string userId, string rootId)
    {
        await CancelTasksForUserAsync(userId);

        var root = await _db.UserRoots.FirstOrDefaultAsync(r => r.Id == rootId && r.UserId == userId);
        if (root == null) return false;

        root.IsDeleted = true;
        root.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _fileWatcher?.UnwatchRoot(userId, rootId);
        return true;
    }

    public bool HasNesting(List<string> roots)
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

    private async Task CancelTasksForUserAsync(string userId)
    {
        if (_scanService?.Status.IsRunning == true)
        {
            _logger.LogInformation("Cancelling scan because user {UserId} root is being modified", userId);
            _scanService?.Cancel();
            for (var i = 0; i < 100 && _scanService?.Status.IsRunning == true; i++)
                await Task.Delay(100);
        }

        if (_thumbnailService?.RegenerationStatus.IsRunning == true)
        {
            _logger.LogInformation("Cancelling thumbnail generation because user {UserId} root is being modified", userId);
            _thumbnailService?.CancelGeneration();
            for (var i = 0; i < 100 && _thumbnailService?.RegenerationStatus.IsRunning == true; i++)
                await Task.Delay(100);
        }
    }
}
