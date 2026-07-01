using System.Collections.Concurrent;
using GalleryCloud.Api.Data;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Enums;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
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
            var users = await db.Users.Where(u => !u.IsDeleted).ToListAsync(linkedCt);

            foreach (var user in users)
            {
                if (linkedCt.IsCancellationRequested) break;
                Status = Status with { UserId = user.Id };
                await RunScanAsync(user.Id, ScanMode.Full, linkedCt);
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
            await RunScanAsync(userId, mode, linkedCt);
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

    private async Task RunScanAsync(string userId, ScanMode mode, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var roots = await db.UserRoots
            .Where(r => r.UserId == userId && r.IsEnabled && !r.IsDeleted)
            .ToListAsync(ct);

        if (roots.Count == 0)
        {
            _logger.LogWarning("No enabled roots found for user {UserId}", userId);
            return;
        }

        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) break;
            await ScanRootAsync(db, root, mode, ct);
        }
    }

    private async Task ScanRootAsync(AppDbContext db, UserRoot root, ScanMode mode, CancellationToken ct)
    {
        using var innerScope = _scopeFactory.CreateScope();
        var thumbDb = innerScope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();
        var rootPath = Path.GetFullPath(root.RootPath);
        if (!Directory.Exists(rootPath))
        {
            _logger.LogWarning("Root path does not exist: {RootPath} (rootId={RootId})", rootPath, root.Id);
            return;
        }

        var formatsStr = await _settings.GetAsync(SettingKeys.ScanSupportedFormats) ?? ".jpg,.jpeg,.heic,.avif,.png,.webp";
        var supportedFormats = formatsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim().ToLowerInvariant())
            .ToHashSet();

        var excludeStr = await _settings.GetAsync(SettingKeys.ScanExcludePatterns) ?? "**/thumbnails/**";
        var excludeGlobs = excludeStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).ToList();

        var parallelThreads = await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 2);
        var semaphore = new SemaphoreSlim(Math.Max(1, parallelThreads));

        // Enumerate all files
        _logger.LogInformation("Enumerating files in {RootPath}...", rootPath);
        Status = Status with { TotalFiles = 0, ProcessedFiles = -1 };
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

        _logger.LogInformation("Found {Count} files to scan for root {RootId}", allFiles.Count, root.Id);
        Status = Status with { TotalFiles = allFiles.Count, ProcessedFiles = 0 };

        var log = new ScanLog
        {
            UserId = root.UserId,
            StartedAt = DateTime.UtcNow,
            Mode = mode == ScanMode.Full ? "full" : "incremental",
            TotalFound = allFiles.Count
        };
        db.ScanLogs.Add(log);

        // Existing photos for this root
        var existingPhotos = await db.Photos
            .Where(p => p.UserId == root.UserId && p.RootId == root.Id)
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
                    UserId = root.UserId,
                    RootId = root.Id,
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
                    ExposureTime = exif.ExposureTime,
                    Iso = exif.Iso,
                    Aperture = exif.Aperture,
                    FocalLength = exif.FocalLength,
                    FocalLength35mm = exif.FocalLength35mm,
                    Latitude = exif.Latitude,
                    Longitude = exif.Longitude,
                    IsDeleted = false,
                };

                newPhotos.Add(photo);
                var added = Interlocked.Increment(ref newAdded);
                if (added % 100 == 0)
                    _logger.LogInformation("Scanned {Count}/{Total} for root {RootId}", added, allFiles.Count, root.Id);
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
                    var oldThumbs = await thumbDb.ThumbnailCaches.Where(t => t.PhotoId == existing.Id).ToListAsync(ct);
                    thumbDb.ThumbnailCaches.RemoveRange(oldThumbs);
                    await thumbDb.SaveChangesAsync(ct);
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

        // Soft-delete missing files for this root
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
            "Scan complete for root {RootId}: {Total} files, {New} new, {Skipped} skipped, {Deleted} deleted, {Errors} errors",
            root.Id, allFiles.Count, newAdded, skipped, softDeleted, errors);
    }

    public async Task RefreshExifAsync(string userId, CancellationToken ct = default)
    {
        Status = new ScanStatus { IsRunning = true, Mode = "refreshexif", UserId = userId, StartedAt = DateTime.UtcNow };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var photos = await db.Photos
                .Where(p => p.UserId == userId && !p.IsDeleted)
                .ToListAsync(ct);

            int total = photos.Count;
            int processed = 0;
            Status = Status with { TotalFiles = total };

            foreach (var photo in photos)
            {
                if (ct.IsCancellationRequested) break;

                var root = await db.UserRoots.FindAsync(photo.RootId);
                if (root == null) { processed++; continue; }

                var fullPath = Path.GetFullPath(Path.Combine(root.RootPath, photo.FilePath));
                if (!File.Exists(fullPath)) { processed++; continue; }

                try
                {
                    var exif = ExifService.Extract(fullPath);
                    photo.Width = exif.Width;
                    photo.Height = exif.Height;
                    photo.Orientation = exif.Orientation;
                    photo.TakenAt = exif.TakenAt?.ToUniversalTime();
                    photo.DeviceModel = exif.DeviceModel;
                    photo.ExposureTime = exif.ExposureTime;
                    photo.Iso = exif.Iso;
                    photo.Aperture = exif.Aperture;
                    photo.FocalLength = exif.FocalLength;
                    photo.FocalLength35mm = exif.FocalLength35mm;
                    photo.Latitude = exif.Latitude;
                    photo.Longitude = exif.Longitude;
                    photo.UpdatedAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error refreshing EXIF for {Path}", fullPath);
                }

                processed++;
                Status = Status with { ProcessedFiles = processed };
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("EXIF refresh complete: {Count} photos updated for user {User}", total, userId);
        }
        finally
        {
            Status = new ScanStatus();
        }
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