using System.Collections.Concurrent;
using System.Threading.Channels;
using GalleryCloud.Api.Data;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Services;

public class FileWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingService _settings;
    private readonly ILogger<FileWatcherService> _logger;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Channel<FileEvent> _eventChannel = Channel.CreateBounded<FileEvent>(10000);
    private HashSet<string> _supportedFormats = [];

    public FileWatcherService(IServiceScopeFactory scopeFactory, ISettingService settings, ILogger<FileWatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    private async Task LoadSupportedFormatsAsync()
    {
        var raw = await _settings.GetAsync(SettingKeys.ScanSupportedFormats) ?? string.Empty;
        _supportedFormats = raw.Split(',')
            .Select(SettingKeys.NormalizeFormat)
            .ToHashSet();
    }

    public async Task InitializeAsync()
    {
        await LoadSupportedFormatsAsync();
        var enabled = await _settings.GetAsync(SettingKeys.FileWatcherEnabled, true);
        if (!enabled) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roots = await db.UserRoots
            .Where(r => r.IsEnabled && !r.IsDeleted && r.User != null && !r.User.IsDeleted)
            .ToListAsync();

        foreach (var root in roots)
        {
            WatchRoot(root);
        }
    }

    public void WatchRoot(UserRoot root)
    {
        var key = $"{root.UserId}:{root.Id}";
        if (_watchers.ContainsKey(key)) return;

        var rootPath = Path.GetFullPath(root.RootPath);
        if (!Directory.Exists(rootPath)) return;

        try
        {
            var watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                    | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 65536,
                EnableRaisingEvents = true,
            };

            watcher.Created += (_, e) => Enqueue(root.UserId, root.Id, rootPath, "created", e.FullPath);
            watcher.Changed += (_, e) => Enqueue(root.UserId, root.Id, rootPath, "changed", e.FullPath);
            watcher.Deleted += (_, e) => Enqueue(root.UserId, root.Id, rootPath, "deleted", e.FullPath);
            watcher.Renamed += (_, e) => Enqueue(root.UserId, root.Id, rootPath, "renamed", e.FullPath, e.OldFullPath);
            watcher.Error += (_, e) => _logger.LogError(e.GetException(), "FileWatcher error for root {RootId}", root.Id);

            _watchers[key] = watcher;
            _logger.LogInformation("FileWatcher started for root {RootId}: {RootPath}", root.Id, rootPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start FileWatcher for {RootPath}", rootPath);
        }
    }

    public void UnwatchRoot(string userId, string rootId)
    {
        var key = $"{userId}:{rootId}";
        if (_watchers.TryRemove(key, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    public void StopAll()
    {
        foreach (var key in _watchers.Keys)
        {
            if (_watchers.TryRemove(key, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }
        _logger.LogInformation("FileWatcher: all watchers stopped");
    }

    private void Enqueue(string userId, string rootId, string rootPath, string changeType, string fullPath, string? oldPath = null)
    {
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext.Length > 0 && !_supportedFormats.Contains(ext)) return;

        var relative = fullPath[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (oldPath != null)
        {
            var oldRelative = oldPath[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _logger.LogInformation("FileWatcher detect [{Type}] {OldPath} → {Path}", changeType, oldRelative, relative);
        }
        else
        {
            _logger.LogInformation("FileWatcher detect [{Type}] {Path}", changeType, relative);
        }
        _eventChannel.Writer.TryWrite(new FileEvent(userId, rootId, rootPath, changeType, fullPath, oldPath));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var debounceMs = await _settings.GetAsync(SettingKeys.FileWatcherDebounceDelayMs, 5000);
        var batch = new Dictionary<string, List<FileEvent>>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for first event
                var firstEvent = await _eventChannel.Reader.ReadAsync(stoppingToken);
                batch[GetEventKey(firstEvent)] = [firstEvent];

                // Debounce: collect events for debounceMs milliseconds
                var deadline = DateTime.UtcNow.AddMilliseconds(debounceMs);
                while (DateTime.UtcNow < deadline)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    using var cts = new CancellationTokenSource(remaining);
                    try
                    {
                        var nextEvent = await _eventChannel.Reader.ReadAsync(cts.Token);
                        var key = GetEventKey(nextEvent);
                        if (!batch.TryGetValue(key, out var events))
                            batch[key] = events = [];
                        events.Add(nextEvent); // Preserve event order per file
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // Process batch in order
                _logger.LogInformation("FileWatcher processing {Count} events", batch.Sum(kv => kv.Value.Count));
                await ProcessBatchAsync(batch.Values.SelectMany(v => v).ToList(), stoppingToken);
                batch.Clear();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file events");
            }
        }
    }

    private async Task ProcessBatchAsync(List<FileEvent> events, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedCount = 0;

        foreach (var evt in events)
        {
            var relativePath = evt.FullPath[evt.RootPath.Length..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');

            var ext = Path.GetExtension(relativePath).ToLowerInvariant();
            if (ext.Length > 0 && !_supportedFormats.Contains(ext)) continue;

            switch (evt.ChangeType)
            {
                case "created":
                case "changed":
                    savedCount += await HandleAddOrUpdate(db, evt.UserId, evt.RootId, evt.RootPath, relativePath, evt.FullPath, ct);
                    break;

                case "deleted":
                    savedCount += await HandleDelete(db, evt.UserId, evt.RootId, relativePath, ct);
                    break;

                case "renamed" when evt.OldPath != null:
                    var oldRelative = evt.OldPath[evt.RootPath.Length..]
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    savedCount += await HandleRename(db, evt.UserId, evt.RootId, oldRelative, relativePath, evt.FullPath, ct);
                    break;
            }
        }

        if (savedCount > 0)
        {
            ct.ThrowIfCancellationRequested();
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("FileWatcher batch saved {Count} changes", savedCount);
        }
    }

    /// <returns>1 if a change was made, 0 if skipped</returns>
    private async Task<int> HandleAddOrUpdate(AppDbContext db, string userId, string rootId, string rootPath,
        string relativePath, string fullPath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(fullPath)) return 0;

            var fileInfo = new FileInfo(fullPath);
            var exifEngine = await _settings.GetAsync(SettingKeys.ImageProcessingEngine, "ImageSharp");
            var exif = ExifService.Extract(fullPath, exifEngine, fileInfo.LastWriteTimeUtc);

            var photo = await db.Photos
                .FirstOrDefaultAsync(p => p.UserId == userId && p.RootId == rootId && p.FilePath == relativePath, ct);

            if (photo == null)
            {
                photo = new Photo
                {
                    UserId = userId,
                    RootId = rootId,
                    FilePath = relativePath,
                    FileName = Path.GetFileName(relativePath),
                    FileSize = fileInfo.Length,
                    FileFormat = Path.GetExtension(relativePath).ToLowerInvariant(),
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
                db.Photos.Add(photo);
                _logger.LogInformation("FileWatcher created: {Path}", relativePath);
                return 1;
            }
            else
            {
                photo.FileName = Path.GetFileName(relativePath);
                photo.FileFormat = Path.GetExtension(relativePath).ToLowerInvariant();
                photo.FileSize = fileInfo.Length;
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
                photo.IsDeleted = false;
                photo.DeletedAt = null;
                photo.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("FileWatcher updated: {Path}", relativePath);
                return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing file: {Path}", fullPath);
            return 0;
        }
    }

    /// <returns>1 if a change was made, 0 if skipped</returns>
    private async Task<int> HandleDelete(AppDbContext db, string userId, string rootId, string relativePath, CancellationToken ct)
    {
        var photo = await db.Photos
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RootId == rootId && p.FilePath == relativePath && !p.IsDeleted, ct);

        if (photo != null)
        {
            photo.IsDeleted = true;
            photo.DeletedAt = DateTime.UtcNow;
            photo.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("FileWatcher deleted: {Path}", relativePath);
            return 1;
        }
        return 0;
    }

    /// <returns>1 if a change was made, 0 if skipped</returns>
    private async Task<int> HandleRename(AppDbContext db, string userId, string rootId, string oldPath,
        string newPath, string fullPath, CancellationToken ct)
    {
        var photo = await db.Photos
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RootId == rootId && p.FilePath == oldPath, ct);

        if (photo == null) return 0;

        photo.FilePath = newPath;
        photo.FileName = Path.GetFileName(newPath);
        photo.UpdatedAt = DateTime.UtcNow;
        _logger.LogInformation("FileWatcher renamed: {OldPath} → {NewPath}", oldPath, newPath);
        return 1;
    }

    private static string GetEventKey(FileEvent evt) => $"{evt.UserId}|{evt.RootId}|{evt.FullPath}";

    private record FileEvent(string UserId, string RootId, string RootPath, string ChangeType, string FullPath, string? OldPath = null);
}