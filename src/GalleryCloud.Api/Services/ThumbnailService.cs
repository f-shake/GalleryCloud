using System.Collections.Concurrent;
using GalleryCloud.Api.Data;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Enums;
using GalleryCloud.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace GalleryCloud.Api.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingService _settings;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _photoLocks = new();

    // Separate semaphores: preview has dedicated slots, grid uses the rest
    private SemaphoreSlim _gridSemaphore = new(2, 2);
    private SemaphoreSlim _previewSemaphore = new(2, 2);
    private readonly int _gridMax = 2;
    private readonly int _previewMax = 2;
    private int _gridWaiting = 0;
    private int _previewWaiting = 0;

    private ThumbnailGenerationStatus _regenerationStatus = new();
    private CancellationTokenSource? _regenerationCts;

    public ThumbnailGenerationStatus RegenerationStatus
    {
        get => _regenerationStatus with { };
        private set => _regenerationStatus = value;
    }

    public ThumbnailService(IServiceScopeFactory scopeFactory, ISettingService settings,
        IMemoryCache memoryCache, ILogger<ThumbnailService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<Stream?> GetThumbnailAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sizeKey = size.ToString().ToLowerInvariant();

        // Check DB cache
        var cacheRecord = await db.ThumbnailCaches
            .FirstOrDefaultAsync(t => t.PhotoId == photoId && t.Size == sizeKey);

        if (cacheRecord != null && File.Exists(cacheRecord.FilePath))
        {
            // Memory cache check
            var memKey = $"thumb:{photoId}:{sizeKey}";
            if (_memoryCache.TryGetValue(memKey, out byte[]? cached) && cached != null)
                return new MemoryStream(cached);

            var fileStream = new MemoryStream(await File.ReadAllBytesAsync(cacheRecord.FilePath));
            CacheInMemory(memKey, fileStream.ToArray());
            fileStream.Position = 0;
            return fileStream;
        }

        // Photo must exist
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted);
        if (photo == null) return null;

        using var userScope = _scopeFactory.CreateScope();
        var userCtx = userScope.ServiceProvider.GetRequiredService<IUserContext>();

        // Load user to get RootPath (photo belongs to specific user)
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == photo.UserId);
        if (user == null) return null;

        var fullPath = Path.GetFullPath(Path.Combine(user.RootPath, photo.FilePath));
        if (!File.Exists(fullPath)) return null;

        // Global concurrency: preview has dedicated slots, grid queues separately
        var isPreview = sizeKey == "preview";
        var globalSem = isPreview ? _previewSemaphore : _gridSemaphore;

        int waitingBefore;
        if (isPreview) waitingBefore = Interlocked.Increment(ref _previewWaiting);
        else waitingBefore = Interlocked.Increment(ref _gridWaiting);
        if (waitingBefore > 1)
            _logger.LogInformation("缩略图队列 [{Size}] {PhotoId}: {Waiting} 排队中", sizeKey, photoId[..8], waitingBefore);

        await globalSem.WaitAsync(ct);

        var active = 0;
        if (isPreview) { active = _previewMax - _previewSemaphore.CurrentCount; Interlocked.Decrement(ref _previewWaiting); }
        else { active = _gridMax - _gridSemaphore.CurrentCount; Interlocked.Decrement(ref _gridWaiting); }
        if (active > 1 || waitingBefore > 1)
            _logger.LogInformation("缩略图生成 [{Size}] {PhotoId}: {Active} 并发", sizeKey, photoId[..8], active);
        try
        {
        // Per-photo lock to prevent duplicate generation
        var semaphore = _photoLocks.GetOrAdd(photoId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            // Double-check cache after acquiring lock
            cacheRecord = await db.ThumbnailCaches
                .FirstOrDefaultAsync(t => t.PhotoId == photoId && t.Size == sizeKey);
            if (cacheRecord != null && File.Exists(cacheRecord.FilePath))
            {
                var bytes = await File.ReadAllBytesAsync(cacheRecord.FilePath);
                return new MemoryStream(bytes);
            }

            // Generate — pick settings based on size
            var isPreviewGen = sizeKey == "preview";
            var format = await _settings.GetAsync(isPreviewGen ? "preview.format" : "thumbnail.format", "webp");
            var quality = await _settings.GetAsync(isPreviewGen ? "preview.quality" : "thumbnail.quality", 80);
            var cacheDir = await _settings.GetAsync("thumbnail.cacheDir", "data/thumbnails");

            var cacheAbsoluteDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), cacheDir, user.Id, sizeKey));
            Directory.CreateDirectory(cacheAbsoluteDir);

            var extension = format.ToLowerInvariant() switch
            {
                "jpg" or "jpeg" => ".jpg",
                _ => ".webp"
            };

            var cachePath = Path.Combine(cacheAbsoluteDir, $"{photoId}_{width}{extension}");

            ct.ThrowIfCancellationRequested();
            using var image = await Image.LoadAsync(fullPath, ct);
            IImageEncoder encoder = format.ToLowerInvariant() switch
            {
                "jpg" or "jpeg" => new JpegEncoder { Quality = quality },
                _ => new WebpEncoder { Quality = quality }
            };

            // Apply EXIF orientation first, then resize
            image.Mutate(x => x.AutoOrient());

            // Clamp to max resolution for preview
            var targetW = width;
            if (isPreviewGen)
            {
                var maxRes = await _settings.GetAsync("preview.maxResolution", 2560);
                targetW = Math.Min(targetW, maxRes);
            }
            targetW = Math.Min(targetW, image.Width);
            image.Mutate(x => x.Resize(targetW, 0));

            await image.SaveAsync(cachePath, encoder);

            // Save cache record
            var record = await db.ThumbnailCaches
                .FirstOrDefaultAsync(t => t.PhotoId == photoId && t.Size == sizeKey);
            if (record == null)
            {
                record = new ThumbnailCache
                {
                    PhotoId = photoId,
                    Size = sizeKey,
                    Format = format,
                    FilePath = cachePath,
                };
                db.ThumbnailCaches.Add(record);
            }
            else
            {
                record.Format = format;
                record.FilePath = cachePath;
                record.CreatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("缩略图完成 [{Size}] {PhotoId}", sizeKey, photoId[..8]);

            var resultBytes = await File.ReadAllBytesAsync(cachePath);
            CacheInMemory($"thumb:{photoId}:{sizeKey}", resultBytes);
            return new MemoryStream(resultBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail for photo {PhotoId}", photoId);
            return null;
        }
        finally
        {
            semaphore.Release();
        }
        }
        finally
        {
            globalSem.Release();
        }
    }

    public async Task RegenerateAllAsync(CancellationToken ct = default)
    {
        if (RegenerationStatus.IsRunning) return;

        _regenerationCts?.Cancel();
        _regenerationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Clear existing cache
        var allCache = await db.ThumbnailCaches.ToListAsync(_regenerationCts.Token);
        db.ThumbnailCaches.RemoveRange(allCache);
        await db.SaveChangesAsync(_regenerationCts.Token);

        // Clear disk cache
        var cacheDir = await _settings.GetAsync("thumbnail.cacheDir", "data/thumbnails");
        var absoluteDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), cacheDir));
        if (Directory.Exists(absoluteDir))
        {
            Directory.Delete(absoluteDir, true);
            Directory.CreateDirectory(absoluteDir);
        }

        var parallelThreads = await _settings.GetAsync("thumbnail.parallelThreads", 2);
        var semaphore = new SemaphoreSlim(Math.Max(1, parallelThreads));

        var photoIds = await db.Photos.Where(p => !p.IsDeleted).Select(p => p.Id).ToListAsync(_regenerationCts.Token);
        RegenerationStatus = new ThumbnailGenerationStatus { IsRunning = true, Total = photoIds.Count * 3, Processed = 0 };

        var sizes = new[] { (ThumbnailSize.Grid, 400), (ThumbnailSize.Preview, 1200), (ThumbnailSize.Full, 2560) };

        foreach (var photoId in photoIds)
        {
            if (_regenerationCts.Token.IsCancellationRequested) break;

            foreach (var (size, w) in sizes)
            {
                if (_regenerationCts.Token.IsCancellationRequested) break;

                await semaphore.WaitAsync(_regenerationCts.Token);
                try
                {
                    await GetThumbnailAsync(photoId, size, w);
                }
                catch { /* skip errors during batch generation */ }
                finally
                {
                    semaphore.Release();
                }

                RegenerationStatus = RegenerationStatus with
                {
                    Processed = RegenerationStatus.Processed + 1
                };
            }
        }

        RegenerationStatus = new ThumbnailGenerationStatus();
    }

    private void CacheInMemory(string key, byte[] data)
    {
        var maxMb = 512; // default
        try
        {
            var entryOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                Size = data.Length,
            };
            _memoryCache.Set(key, data, entryOptions);
        }
        catch
        {
            // Cache full, ignore
        }
    }
}
