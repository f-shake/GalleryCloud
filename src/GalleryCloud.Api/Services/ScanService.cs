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
    private readonly object _startLock = new();
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

    public Task TriggerFullScanAsync(string userId, CancellationToken ct = default)
        => RunWithStatusLockedAsync(userId, ScanMode.Full, ct);

    public async Task TriggerFullScanForAllUsersAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        lock (_startLock)
        {
            if (Status.IsRunning) return;
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            Status = new ScanStatus { IsRunning = true, Mode = "full", StartedAt = DateTime.UtcNow };
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = await db.Users.Where(u => !u.IsDeleted).ToListAsync(cts.Token);

            int cumulativeTotal = 0;
            int cumulativeProcessed = 0;
            foreach (var user in users)
            {
                if (cts.Token.IsCancellationRequested) break;
                Status = Status with { UserId = user.Id };
                (cumulativeTotal, cumulativeProcessed) = await RunScanAsync(user.Id, ScanMode.Full, cumulativeTotal, cumulativeProcessed, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scan cancelled by user");
        }
        finally
        {
            lock (_startLock)
            {
                _cts?.Dispose();
                _cts = null;
                Status = new ScanStatus();
            }
        }
    }

    private async Task RunWithStatusLockedAsync(string userId, ScanMode mode, CancellationToken ct)
    {
        CancellationTokenSource? cts;
        lock (_startLock)
        {
            if (Status.IsRunning) return;
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            Status = new ScanStatus { IsRunning = true, Mode = mode.ToString().ToLower(), UserId = userId, StartedAt = DateTime.UtcNow };
        }

        try
        {
            await RunScanAsync(userId, mode, 0, 0, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scan cancelled by user");
        }
        finally
        {
            lock (_startLock)
            {
                _cts?.Dispose();
                _cts = null;
                Status = new ScanStatus();
            }
        }
    }

    private async Task<(int Total, int Processed)> RunScanAsync(string userId, ScanMode mode,
        int cumulativeTotal, int cumulativeProcessed, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var roots = await db.UserRoots
            .Where(r => r.UserId == userId && r.IsEnabled && !r.IsDeleted)
            .ToListAsync(ct);

        if (roots.Count == 0)
        {
            _logger.LogWarning("No enabled roots found for user {UserId}", userId);
            return (0, 0);
        }

        // First pass: enumerate files across all roots to know the grand total upfront
        var formatsStr = await _settings.GetAsync(SettingKeys.ScanSupportedFormats) ?? string.Empty;
        var supportedFormats = formatsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(SettingKeys.NormalizeFormat)
            .ToHashSet();
        var excludeStr = await _settings.GetAsync(SettingKeys.ScanExcludePatterns) ?? "**/thumbnails/**";
        var excludeGlobs = excludeStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).ToList();

        int grandTotal = 0;
        var filesByRoot = new Dictionary<string, List<string>>();
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
        };

        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) break;
            var rootPath = Path.GetFullPath(root.RootPath);
            if (!Directory.Exists(rootPath))
            {
                _logger.LogWarning("Root path does not exist: {RootPath} (rootId={RootId})", rootPath, root.Id);
                continue;
            }

            var rootFiles = new List<string>();
            filesByRoot[root.Id] = rootFiles;

            foreach (var fmt in supportedFormats)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var files = Directory.GetFiles(rootPath, $"*{fmt}", enumOptions)
                        .Where(f => !IsExcluded(f, rootPath, excludeGlobs))
                        .ToList();
                    rootFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning format {Format} in {Root}", fmt, rootPath);
                }
            }

            grandTotal += rootFiles.Count;
        }

        if (grandTotal == 0)
        {
            _logger.LogWarning("No files found for user {UserId}", userId);
            return (0, 0);
        }

        // Set the grand total so the progress bar goes 0→100 once
        Status = Status with { TotalFiles = cumulativeTotal + grandTotal };
        _logger.LogInformation("Enumeration complete: grandTotal={GrandTotal}, cumulativeTotal={Cum}, TotalFiles={Total}",
            grandTotal, cumulativeTotal, cumulativeTotal + grandTotal);

        // Second pass: process each root with the pre-enumerated file list
        foreach (var root in roots)
        {
            if (ct.IsCancellationRequested) break;
            if (!filesByRoot.TryGetValue(root.Id, out var rootFiles) || rootFiles.Count == 0)
                continue;

            var result = await ScanRootAsync(db, root, mode, cumulativeTotal, cumulativeProcessed,
                rootFiles, ct);
            cumulativeTotal += result.Total;
            cumulativeProcessed += result.Processed;
            Status = Status with { ProcessedFiles = cumulativeProcessed };
            _logger.LogInformation("Progress after root {RootId}: ProcessedFiles={Cum}", root.Id[..8], cumulativeProcessed);
        }

        return (cumulativeTotal, cumulativeProcessed);
    }

    private async Task<(int Total, int Processed)> ScanRootAsync(AppDbContext db, UserRoot root, ScanMode mode,
        int cumulativeTotal, int cumulativeProcessed, List<string> allFiles, CancellationToken ct)
    {
        using var innerScope = _scopeFactory.CreateScope();
        var thumbDb = innerScope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();
        var rootPath = Path.GetFullPath(root.RootPath);

        var parallelThreads = await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 2);
        var semaphore = new SemaphoreSlim(Math.Max(1, parallelThreads));
        var exifEngine = await _settings.GetAsync(SettingKeys.ImageProcessingEngine, "ImageSharp");

        _logger.LogInformation("Processing {Count} files for root {RootId}", allFiles.Count, root.Id);
        var rootTotal = allFiles.Count;

        var log = new ScanLog
        {
            UserId = root.UserId,
            StartedAt = DateTime.UtcNow,
            Mode = mode == ScanMode.Full ? "full" : "incremental",
            TotalFound = rootTotal
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

                var fileInfo2 = new FileInfo(fullPath);
                var exif = ExifService.Extract(fullPath, exifEngine, fileInfo2.LastWriteTimeUtc);
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
                    TakenAt = exif.TakenAt,
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
                    _logger.LogInformation("Scanned {Count}/{RootTotal} in root {RootId}, cumulative ProcessedFiles={Cumulative}",
                        added, rootTotal, root.Id[..8], cumulativeProcessed + added);
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
                Status = Status with { ProcessedFiles = cumulativeProcessed + current };
            }
        });

        var totalProcessed = cumulativeProcessed + processedCount;
        Status = Status with { ProcessedFiles = totalProcessed };

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
            root.Id, rootTotal, newAdded, skipped, softDeleted, errors);

        return (rootTotal, processedCount);
    }

    public async Task RefreshExifAsync(string userId, CancellationToken ct = default)
    {
        lock (_startLock)
        {
            if (Status.IsRunning) return;
            Status = new ScanStatus { IsRunning = true, Mode = "refreshexif", UserId = userId, StartedAt = DateTime.UtcNow };
        }

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
            var exifEngine = await _settings.GetAsync(SettingKeys.ImageProcessingEngine, "ImageSharp");

            foreach (var photo in photos)
            {
                if (ct.IsCancellationRequested) break;

                var root = await db.UserRoots.FindAsync(photo.RootId);
                if (root == null) { processed++; continue; }

                var fullPath = Path.GetFullPath(Path.Combine(root.RootPath, photo.FilePath));
                if (!File.Exists(fullPath)) { processed++; continue; }

                try
                {
                    var exif = ExifService.Extract(fullPath, exifEngine, File.GetLastWriteTimeUtc(fullPath));
                    photo.Width = exif.Width;
                    photo.Height = exif.Height;
                    photo.Orientation = exif.Orientation;
                    photo.TakenAt = exif.TakenAt;
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
                if (processed % 100 == 0)
                    _logger.LogInformation("EXIF refresh: {Processed}/{Total}", processed, total);
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("EXIF refresh complete: {Count} photos updated for user {User}", total, userId);
        }
        finally
        {
            lock (_startLock)
            {
                Status = new ScanStatus();
            }
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