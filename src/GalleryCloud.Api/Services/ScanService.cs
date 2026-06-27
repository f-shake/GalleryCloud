using System.Collections.Concurrent;
using GalleryCloud.Api.Data;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Enums;
using GalleryCloud.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Services;

public class ScanService : IScanService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingService _settings;
    private readonly ILogger<ScanService> _logger;
    private readonly object _statusLock = new();
    private ScanStatus _status = new();

    public ScanStatus Status
    {
        get { lock (_statusLock) return _status with { }; }
        private set { lock (_statusLock) _status = value; }
    }

    public ScanService(IServiceScopeFactory scopeFactory, ISettingService settings, ILogger<ScanService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task TriggerFullScanAsync(string userId, CancellationToken ct = default)
    {
        if (Status.IsRunning) return;

        Status = new ScanStatus { IsRunning = true, Mode = "full", UserId = userId, StartedAt = DateTime.UtcNow };

        try
        {
            await RunScanAsync(userId, ScanMode.Full, ct);
        }
        finally
        {
            Status = new ScanStatus();
        }
    }

    public async Task TriggerIncrementalScanAsync(string userId, CancellationToken ct = default)
    {
        if (Status.IsRunning) return;

        Status = new ScanStatus { IsRunning = true, Mode = "incremental", UserId = userId, StartedAt = DateTime.UtcNow };

        try
        {
            await RunScanAsync(userId, ScanMode.Incremental, ct);
        }
        finally
        {
            Status = new ScanStatus();
        }
    }

    public async Task TriggerFullScanForAllUsersAsync(CancellationToken ct = default)
    {
        if (Status.IsRunning) return;

        Status = new ScanStatus { IsRunning = true, Mode = "full", StartedAt = DateTime.UtcNow };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userIds = await db.Users.Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct);

            Status = Status with { TotalFiles = userIds.Count * 1000 }; // rough estimate

            foreach (var userId in userIds)
            {
                if (ct.IsCancellationRequested) break;
                Status = Status with { UserId = userId };
                await RunScanAsync(userId, ScanMode.Full, ct);
            }
        }
        finally
        {
            Status = new ScanStatus();
        }
    }

    private async Task RunScanAsync(string userId, ScanMode mode, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return;

        // Load scan settings from DB
        var formatsStr = await _settings.GetAsync("scan.supportedFormats") ?? ".jpg,.jpeg,.heic,.avif,.png,.webp";
        var supportedFormats = formatsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim().ToLowerInvariant())
            .ToHashSet();

        var excludeStr = await _settings.GetAsync("scan.excludePatterns") ?? "**/thumbnails/**";
        var excludeGlobs = excludeStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();

        // Thread limit for scanning
        var parallelThreads = await _settings.GetAsync("thumbnail.parallelThreads", 2);
        var semaphore = new SemaphoreSlim(Math.Max(1, parallelThreads));

        var rootPath = Path.GetFullPath(user.RootPath);
        if (!Directory.Exists(rootPath))
        {
            _logger.LogWarning("Root path does not exist: {RootPath}", rootPath);
            return;
        }

        // Get all files
        var allFiles = new List<string>();
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
        };

        foreach (var fmt in supportedFormats)
        {
            try
            {
                var files = Directory.GetFiles(rootPath, $"*{fmt}", enumOptions)
                    .Where(f => !IsExcluded(f, rootPath, excludeGlobs))
                    .ToList();
                allFiles.AddRange(files);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning format {Format}", fmt);
            }
        }

        // Normalize to relative paths
        var fileRelativePaths = allFiles
            .Select(f => f[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'))
            .ToList();

        Status = Status with { TotalFiles = fileRelativePaths.Count, ProcessedFiles = 0 };

        var log = new ScanLog
        {
            UserId = userId,
            StartedAt = DateTime.UtcNow,
            Mode = mode == ScanMode.Full ? "full" : "incremental",
            TotalFound = fileRelativePaths.Count
        };
        db.ScanLogs.Add(log);

        // Get existing photos for this user
        var existingPhotos = await db.Photos
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.FilePath, p => p, ct);

        int newAdded = 0, softDeleted = 0;

        // Process files in parallel batches
        var newPhotos = new ConcurrentBag<Photo>();
        var processedCount = 0;

        await Parallel.ForEachAsync(fileRelativePaths, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, parallelThreads),
            CancellationToken = ct
        }, async (relativePath, innerCt) =>
        {
            await semaphore.WaitAsync(innerCt);
            try
            {
                var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(fullPath)) return;

                // Check if already exists (skip in incremental mode if unchanged)
                if (existingPhotos.TryGetValue(relativePath, out var existing))
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (existing.FileSize == fileInfo.Length)
                    {
                        Interlocked.Increment(ref processedCount);
                        return; // Unchanged
                    }
                }

                // Parse EXIF
                var exif = ExifService.Extract(fullPath);

                // Compute MD5 (only for new files)
                string? md5 = null;
                if (!existingPhotos.ContainsKey(relativePath))
                {
                    try { md5 = await HashService.ComputeMd5Async(fullPath); }
                    catch { /* ignore hash errors */ }
                }

                var fileInfo2 = new FileInfo(fullPath);

                var photo = new Photo
                {
                    UserId = userId,
                    FilePath = relativePath,
                    FileName = Path.GetFileName(relativePath),
                    FileSize = fileInfo2.Length,
                    FileFormat = Path.GetExtension(relativePath).ToLowerInvariant(),
                    Width = exif.Width,
                    Height = exif.Height,
                    Orientation = exif.Orientation,
                    TakenAt = exif.TakenAt?.ToUniversalTime(),
                    DeviceModel = exif.DeviceModel,
                    Latitude = exif.Latitude,
                    Longitude = exif.Longitude,
                    Md5Hash = md5,
                    IsDeleted = false,
                };

                newPhotos.Add(photo);
                Interlocked.Increment(ref newAdded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing file: {FilePath}", relativePath);
            }
            finally
            {
                semaphore.Release();
                var current = Interlocked.Increment(ref processedCount);
                if (current % 100 == 0)
                {
                    Status = Status with { ProcessedFiles = current };
                }
            }
        });

        Status = Status with { ProcessedFiles = processedCount };

        // Upsert new/updated photos
        foreach (var photo in newPhotos)
        {
            var existing = existingPhotos.GetValueOrDefault(photo.FilePath);
            if (existing != null)
            {
                // Update
                existing.FileSize = photo.FileSize;
                existing.Width = photo.Width;
                existing.Height = photo.Height;
                existing.Orientation = photo.Orientation;
                existing.TakenAt = photo.TakenAt;
                existing.DeviceModel = photo.DeviceModel;
                existing.Latitude = photo.Latitude;
                existing.Longitude = photo.Longitude;
                existing.Md5Hash = photo.Md5Hash;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.Photos.Add(photo);
            }
        }

        // Soft-delete photos whose files no longer exist
        var existingPaths = new HashSet<string>(fileRelativePaths);
        foreach (var (path, photo) in existingPhotos)
        {
            if (!existingPaths.Contains(path) && !photo.IsDeleted)
            {
                photo.IsDeleted = true;
                photo.DeletedAt = DateTime.UtcNow;
                photo.UpdatedAt = DateTime.UtcNow;
                softDeleted++;
            }
        }

        log.NewAdded = newAdded;
        log.SoftDeleted = softDeleted;
        log.FinishedAt = DateTime.UtcNow;
        log.TotalFound = fileRelativePaths.Count;

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Scan complete for user {UserId}: {NewAdded} new, {SoftDeleted} deleted",
            userId, newAdded, softDeleted);
    }

    private static bool IsExcluded(string filePath, string rootPath, List<string> globPatterns)
    {
        var relative = filePath[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');

        foreach (var pattern in globPatterns)
        {
            if (MatchGlob(relative, pattern))
                return true;
        }

        return false;
    }

    private static bool MatchGlob(string path, string pattern)
    {
        // Simple glob matching: ** matches any depth, * matches within segment
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*\*", "___DOUBLESTAR___")
            .Replace(@"\*", "[^/]*")
            .Replace("___DOUBLESTAR___", ".*")
            + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(path, regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
