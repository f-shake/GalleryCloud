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

    public FileWatcherService(IServiceScopeFactory scopeFactory, ISettingService settings, ILogger<FileWatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
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

    private void Enqueue(string userId, string rootId, string rootPath, string changeType, string fullPath, string? oldPath = null)
    {
        _eventChannel.Writer.TryWrite(new FileEvent(userId, rootId, rootPath, changeType, fullPath, oldPath));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var debounceMs = await _settings.GetAsync(SettingKeys.FileWatcherDebounceDelayMs, 5000);
        var batch = new Dictionary<string, FileEvent>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for first event
                var firstEvent = await _eventChannel.Reader.ReadAsync(stoppingToken);
                var key = GetEventKey(firstEvent);
                batch[key] = firstEvent;

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
                        key = GetEventKey(nextEvent);
                        batch[key] = nextEvent; // Last event for a file wins
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // Process batch
                await ProcessBatchAsync(batch.Values.ToList(), stoppingToken);
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

        var formatsStr = await _settings.GetAsync(SettingKeys.ScanSupportedFormats) ?? ".jpg,.jpeg,.heic,.avif,.png,.webp";
        var supportedFormats = formatsStr.Split(',').Select(f => f.Trim().ToLowerInvariant()).ToHashSet();

        foreach (var evt in events)
        {
            var relativePath = evt.FullPath[evt.RootPath.Length..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');

            var ext = Path.GetExtension(relativePath).ToLowerInvariant();
            if (!supportedFormats.Contains(ext)) continue;

            switch (evt.ChangeType)
            {
                case "created":
                case "changed":
                    await HandleAddOrUpdate(db, evt.UserId, evt.RootId, evt.RootPath, relativePath, evt.FullPath, ct);
                    break;

                case "deleted":
                    await HandleDelete(db, evt.UserId, evt.RootId, relativePath, ct);
                    break;

                case "renamed" when evt.OldPath != null:
                    var oldRelative = evt.OldPath[evt.RootPath.Length..]
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    await HandleRename(db, evt.UserId, evt.RootId, oldRelative, relativePath, evt.FullPath, ct);
                    break;
            }
        }
    }

    private async Task HandleAddOrUpdate(AppDbContext db, string userId, string rootId, string rootPath,
        string relativePath, string fullPath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(fullPath)) return;

            var exifEngine = await _settings.GetAsync(SettingKeys.ImageProcessingEngine, "ImageSharp");
            var exif = ExifService.Extract(fullPath, exifEngine);
            var fileInfo = new FileInfo(fullPath);

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
                db.Photos.Add(photo);
            }
            else
            {
                photo.FileSize = fileInfo.Length;
                photo.Width = exif.Width;
                photo.Height = exif.Height;
                photo.Orientation = exif.Orientation;
                photo.TakenAt = exif.TakenAt?.ToUniversalTime();
                photo.DeviceModel = exif.DeviceModel;
                photo.Latitude = exif.Latitude;
                photo.Longitude = exif.Longitude;
                photo.IsDeleted = false;
                photo.DeletedAt = null;
                photo.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing file: {Path}", fullPath);
        }
    }

    private static async Task HandleDelete(AppDbContext db, string userId, string rootId, string relativePath, CancellationToken ct)
    {
        var photo = await db.Photos
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RootId == rootId && p.FilePath == relativePath && !p.IsDeleted, ct);

        if (photo != null)
        {
            photo.IsDeleted = true;
            photo.DeletedAt = DateTime.UtcNow;
            photo.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task HandleRename(AppDbContext db, string userId, string rootId, string oldPath,
        string newPath, string fullPath, CancellationToken ct)
    {
        var photo = await db.Photos
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RootId == rootId && p.FilePath == oldPath, ct);

        if (photo == null) return;

        photo.FilePath = newPath;
        photo.FileName = Path.GetFileName(newPath);
        photo.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string GetEventKey(FileEvent evt) => $"{evt.UserId}|{evt.RootId}|{evt.FullPath}";

    private record FileEvent(string UserId, string RootId, string RootPath, string ChangeType, string FullPath, string? OldPath = null);
}