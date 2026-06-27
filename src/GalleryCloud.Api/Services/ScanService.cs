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
    private CancellationTokenSource? _cts;

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

    public void Cancel()
    {
        _cts?.Cancel();
        _logger.LogInformation("Scan cancellation requested");
    }

    public async Task TriggerFullScanAsync(string userId, CancellationToken ct = default)
    {
        if (Status.IsRunning) return;
        await RunWithStatusAsync(userId, ScanMode.Full, ct);
    }

    public async Task TriggerIncrementalScanAsync(string userId, CancellationToken ct = default)
    {
        if (Status.IsRunning) return;
        await RunWithStatusAsync(userId, ScanMode.Incremental, ct);
    }

    public async Task TriggerFullScanForAllUsersAsync(CancellationToken ct = default)
    {
        if (Status.IsRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = _cts.Token;

        Status = new ScanStatus { IsRunning = true, Mode = "full", StartedAt = DateTime.UtcNow };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = await db.Users.Where(u => u.IsActive).ToListAsync(linkedCt);

            foreach (var user in users)
            {
                if (linkedCt.IsCancellationRequested) break;
                Status = Status with { UserId = user.Id };
                await RunScanAsync(user, ScanMode.Full, linkedCt);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scan cancelled by user");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            Status = new ScanStatus();
        }
    }

    private async Task RunWithStatusAsync(string userId, ScanMode mode, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = _cts.Token;

        Status = new ScanStatus { IsRunning = true, Mode = mode.ToString().ToLower(), UserId = userId, StartedAt = DateTime.UtcNow };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, linkedCt);
            if (user == null) return;

            await RunScanAsync(user, mode, linkedCt);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scan cancelled by user");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            Status = new ScanStatus();
        }
    }

    private async Task RunScanAsync(Core.Entities.User user, ScanMode mode, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var formatsStr = await _settings.GetAsync("scan.supportedFormats") ?? ".jpg,.jpeg,.heic,.avif,.png,.webp";
        var supportedFormats = formatsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim().ToLowerInvariant())
            .ToHashSet();

        var excludeStr = await _settings.GetAsync("scan.excludePatterns") ?? "**/thumbnails/**";
        var excludeGlobs = excludeStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).ToList();

        var parallelThreads = await _settings.GetAsync("thumbnail.parallelThreads", 2);
        var semaphore = new SemaphoreSlim(Math.Max(1, parallelThreads));

        var rootPath = Path.GetFullPath(user.RootPath);
        if (!Directory.Exists(rootPath))
        {
            _logger.LogWarning("Root path does not exist: {RootPath}", rootPath);
            return;
        }

        // Enumerate all files first to get accurate count
        _logger.LogInformation("Enumerating files in {RootPath}...", rootPath);
        Status = Status with { TotalFiles = 0, ProcessedFiles = -1 }; // -1 = enumerating
        var allFiles = new List<string>();
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
        };

        foreach (var fmt in supportedFormats)
        {
            ct.ThrowIfCancellationRequested();
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

        _logger.LogInformation("Found {Count} files to scan for user {User}", allFiles.Count, user.Id);
        Status = Status with { TotalFiles = allFiles.Count, ProcessedFiles = 0 };

        var log = new ScanLog
        {
            UserId = user.Id,
            StartedAt = DateTime.UtcNow,
            Mode = mode == ScanMode.Full ? "full" : "incremental",
            TotalFound = allFiles.Count
        };
        db.ScanLogs.Add(log);

        var existingPhotos = await db.Photos
            .Where(p => p.UserId == user.Id)
            .ToDictionaryAsync(p => p.FilePath, p => p, ct);

        int newAdded = 0, skipped = 0, errors = 0;
        var newPhotos = new ConcurrentBag<Photo>();
        int processedCount = 0;

        await Parallel.ForEachAsync(allFiles, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, parallelThreads),
            CancellationToken = ct
        }, async (fullPath, innerCt) =>
        {
            await semaphore.WaitAsync(innerCt);
            try
            {
                var relativePath = fullPath[rootPath.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');

                if (existingPhotos.TryGetValue(relativePath, out var existing))
                {
                    var fileInfo = new FileInfo(fullPath);
                    var modTime = fileInfo.LastWriteTimeUtc;
                    if (existing.FileSize == fileInfo.Length && existing.FileModifiedAt == modTime)
                    {
                        Interlocked.Increment(ref skipped);
                        return;
                    }
                }

                var exif = ExifService.Extract(fullPath);
                var fileInfo2 = new FileInfo(fullPath);
                var photo = new Photo
                {
                    UserId = user.Id,
                    FilePath = relativePath,
                    FileName = Path.GetFileName(relativePath),
                    FileSize = fileInfo2.Length,
                    FileFormat = Path.GetExtension(relativePath).ToLowerInvariant(),
                    FileModifiedAt = fileInfo2.LastWriteTimeUtc,
                    Width = exif.Width,
                    Height = exif.Height,
                    Orientation = exif.Orientation,
                    TakenAt = exif.TakenAt?.ToUniversalTime(),
                    DeviceModel = exif.DeviceModel,
                    Latitude = exif.Latitude,
                    Longitude = exif.Longitude,
                    IsDeleted = false,
                };

                newPhotos.Add(photo);
                var added = Interlocked.Increment(ref newAdded);
                if (added % 100 == 0)
                    _logger.LogInformation("Scanned {Count}/{Total} for user {User}", added, allFiles.Count, user.Id);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                _logger.LogWarning(ex, "Error processing: {Path}", fullPath);
            }
            finally
            {
                semaphore.Release();
                var current = Interlocked.Increment(ref processedCount);
                Status = Status with { ProcessedFiles = current };
            }
        });

        Status = Status with { ProcessedFiles = processedCount };

        // Upsert new/updated photos
        foreach (var photo in newPhotos)
        {
            var existing = existingPhotos.GetValueOrDefault(photo.FilePath);
            if (existing != null)
            {
                // Invalidate thumbnails if file content changed
                if (existing.FileSize != photo.FileSize || existing.FileModifiedAt != photo.FileModifiedAt)
                {
                    var oldThumbs = await db.ThumbnailCaches.Where(t => t.PhotoId == existing.Id).ToListAsync(ct);
                    db.ThumbnailCaches.RemoveRange(oldThumbs);
                }

                existing.FileSize = photo.FileSize;
                existing.FileModifiedAt = photo.FileModifiedAt;
                existing.Width = photo.Width;
                existing.Height = photo.Height;
                existing.Orientation = photo.Orientation;
                existing.TakenAt = photo.TakenAt;
                existing.DeviceModel = photo.DeviceModel;
                existing.Latitude = photo.Latitude;
                existing.Longitude = photo.Longitude;
                existing.Md5Hash = photo.Md5Hash;
                existing.IsDeleted = false;
                existing.DeletedAt = null;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.Photos.Add(photo);
            }
        }

        // Soft-delete missing files
        int softDeleted = 0;
        var existingPaths = new HashSet<string>(allFiles
            .Select(f => f[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/')));
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

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Scan complete: {Total} files, {New} new, {Skipped} skipped, {Deleted} deleted, {Errors} errors",
            allFiles.Count, newAdded, skipped, softDeleted, errors);
    }

    private static bool IsExcluded(string filePath, string rootPath, List<string> globPatterns)
    {
        var relative = filePath[rootPath.Length..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');

        foreach (var pattern in globPatterns)
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace(@"\*\*", "___DB___")
                .Replace(@"\*", "[^/]*")
                .Replace("___DB___", ".*") + "$";

            if (System.Text.RegularExpressions.Regex.IsMatch(relative, regex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }
}
