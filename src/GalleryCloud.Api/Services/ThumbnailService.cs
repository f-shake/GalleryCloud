using System.Collections.Concurrent;
using System.Threading.Channels;
using GalleryCloud.Api.Data;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Enums;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
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
    private int _queueCount = 0;
    private int _inProgressCount = 0;
    private readonly ConcurrentDictionary<string, byte> _queuedIds = new();
    private readonly Channel<(string PhotoId, ThumbnailSize Size, int Width)> _channel
        = Channel.CreateUnbounded<(string, ThumbnailSize, int)>();

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

    // ── Cache-only check ──────────────────────────────────────
    public async Task<Stream?> TryGetCachedAsync(string photoId, ThumbnailSize size, int width)
    {
        var memKey = $"thumb:{photoId}:{size.ToString().ToLowerInvariant()}";
        if (_memoryCache.TryGetValue(memKey, out byte[]? cached) && cached != null)
            return new MemoryStream(cached);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sizeKey = size.ToString().ToLowerInvariant();

        var cacheRecord = await db.ThumbnailCaches
            .FirstOrDefaultAsync(t => t.PhotoId == photoId && t.Size == sizeKey);

        if (cacheRecord == null || !File.Exists(cacheRecord.FilePath))
            return null;

        var bytes = await File.ReadAllBytesAsync(cacheRecord.FilePath);
        if (bytes.Length == 0)
        {
            // Corrupted cache — clean it up and treat as not cached
            File.Delete(cacheRecord.FilePath);
            return null;
        }
        CacheInMemory(memKey, bytes);
        return new MemoryStream(bytes);
    }

    // ── Enqueue for background generation ─────────────────────
    public void EnqueueAsync(string photoId, ThumbnailSize size, int width)
    {
        var dedupKey = $"{photoId}:{size.ToString().ToLowerInvariant()}";
        if (!_queuedIds.TryAdd(dedupKey, 0))
            return;

        var ok = _channel.Writer.TryWrite((photoId, size, width));
        if (ok)
        {
            var q = Interlocked.Increment(ref _queueCount);
            _logger.LogInformation("缩略图入队 [{Size}] {PhotoId} 排队:{Queue} 并行:{Parallel}",
                size, photoId[..8], q, Math.Max(0, _inProgressCount));
        }
        else
        {
            _queuedIds.TryRemove(dedupKey, out _);
        }
    }

    // ── Background consumer ────────────────────────────────────
    public async Task ConsumeChannelAsync(CancellationToken ct)
    {
        var parallelThreads = await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 4);
        var semaphore = new SemaphoreSlim(Math.Max(1, parallelThreads));

        await foreach (var (photoId, size, width) in _channel.Reader.ReadAllAsync(ct))
        {
            await semaphore.WaitAsync(ct);
            _ = GenerateOneAsync(photoId, size, width, semaphore, ct);
        }
    }

    private async Task GenerateOneAsync(string photoId, ThumbnailSize size, int width,
        SemaphoreSlim semaphore, CancellationToken ct)
    {
        var inFlight = Interlocked.Increment(ref _inProgressCount);
        // Per-photo lock to prevent concurrent writes to the same file
        var perPhotoLock = _photoLocks.GetOrAdd(photoId, _ => new SemaphoreSlim(1, 1));
        await perPhotoLock.WaitAsync(ct);
        try
        {
            var memKey = $"thumb:{photoId}:{size.ToString().ToLowerInvariant()}";
            if (_memoryCache.TryGetValue(memKey, out byte[]? _))
                return;

            await InternalGenerateAsync(photoId, size, width, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background thumbnail failed [{Size}] {PhotoId}", size, photoId[..8]);
        }
        finally
        {
            Interlocked.Decrement(ref _inProgressCount);
            _queuedIds.TryRemove($"{photoId}:{size.ToString().ToLowerInvariant()}", out _);
            perPhotoLock.Release();
            semaphore.Release();
        }
    }

    // ── Full generate (used by background worker & regenerate) ──
    public async Task<Stream?> GetThumbnailAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct = default)
    {
        // Try cache first
        var cached = await TryGetCachedAsync(photoId, size, width);
        if (cached != null) return cached;

        // Fall back to inline generation
        return await InternalGenerateAsync(photoId, size, width, ct);
    }

    private async Task<Stream?> InternalGenerateAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sizeKey = size.ToString().ToLowerInvariant();
        var isPreview = sizeKey == "preview";

        // Photo + user
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted, ct);
        if (photo == null) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == photo.UserId, ct);
        if (user == null) return null;

        var fullPath = Path.GetFullPath(Path.Combine(user.RootPath, photo.FilePath));
        if (!File.Exists(fullPath)) return null;

        var format = "webp";
        var quality = await _settings.GetAsync(isPreview ? SettingKeys.PreviewQuality : SettingKeys.ThumbnailQuality, 80);
        var cacheDir = await _settings.GetAsync(SettingKeys.ThumbnailCacheDir, "data/thumbnails");

        var cacheAbsoluteDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), cacheDir, user.Id, sizeKey));
        Directory.CreateDirectory(cacheAbsoluteDir);

        var cachePath = Path.Combine(cacheAbsoluteDir, $"{photoId}_{width}.webp");

        // Decode + process
        using var image = await Image.LoadAsync(fullPath, ct);
        var encoder = new WebpEncoder { Quality = quality };

        image.Mutate(x => x.AutoOrient());
        var targetW = Math.Min(width, image.Width);
        if (isPreview)
        {
            var maxRes = await _settings.GetAsync(SettingKeys.PreviewMaxResolution, 2560);
            targetW = Math.Min(targetW, maxRes);
        }
        image.Mutate(x => x.Resize(Math.Min(targetW, image.Width), 0));
        await image.SaveAsync(cachePath, encoder, ct);

        // Save cache record
        var record = await db.ThumbnailCaches
            .FirstOrDefaultAsync(t => t.PhotoId == photoId && t.Size == sizeKey, ct);
        if (record == null)
        {
            record = new ThumbnailCache { PhotoId = photoId, Size = sizeKey, Format = format, FilePath = cachePath };
            db.ThumbnailCaches.Add(record);
        }
        else
        {
            record.FilePath = cachePath;
            record.CreatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        var resultBytes = await File.ReadAllBytesAsync(cachePath, ct);
        CacheInMemory($"thumb:{photoId}:{sizeKey}", resultBytes);
        var remaining = Interlocked.Decrement(ref _queueCount);
        _logger.LogInformation("缩略图完成 [{Size}] {PhotoId} 排队:{Queue} 并行:{Parallel}",
            sizeKey, photoId[..8], Math.Max(0, remaining), Math.Max(0, _inProgressCount));

        return new MemoryStream(resultBytes);
    }

    // ── Regenerate all ─────────────────────────────────────────
    public async Task RegenerateAllAsync(CancellationToken ct = default)
    {
        if (RegenerationStatus.IsRunning) return;

        _regenerationCts?.Cancel();
        _regenerationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var allCache = await db.ThumbnailCaches.ToListAsync(_regenerationCts.Token);
        db.ThumbnailCaches.RemoveRange(allCache);
        await db.SaveChangesAsync(_regenerationCts.Token);

        var cacheDir = await _settings.GetAsync(SettingKeys.ThumbnailCacheDir, "data/thumbnails");
        var absoluteDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), cacheDir));
        if (Directory.Exists(absoluteDir)) { Directory.Delete(absoluteDir, true); Directory.CreateDirectory(absoluteDir); }

        var photoIds = await db.Photos.Where(p => !p.IsDeleted).Select(p => p.Id).ToListAsync(_regenerationCts.Token);
        RegenerationStatus = new ThumbnailGenerationStatus { IsRunning = true, Total = photoIds.Count * 3, Processed = 0 };
        var semaphore = new SemaphoreSlim(2);
        var sizes = new[] { (ThumbnailSize.Grid, 400), (ThumbnailSize.Preview, 1200), (ThumbnailSize.Full, 2560) };

        foreach (var pid in photoIds)
        {
            if (_regenerationCts.Token.IsCancellationRequested) break;
            foreach (var (sz, w) in sizes)
            {
                if (_regenerationCts.Token.IsCancellationRequested) break;
                await semaphore.WaitAsync(_regenerationCts.Token);
                try { await InternalGenerateAsync(pid, sz, w, _regenerationCts.Token); }
                catch { }
                finally { semaphore.Release(); }
                RegenerationStatus = RegenerationStatus with { Processed = RegenerationStatus.Processed + 1 };
            }
        }
        RegenerationStatus = new ThumbnailGenerationStatus();
    }

    private void CacheInMemory(string key, byte[] data)
    {
        try
        {
            var entryOptions = new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30), Size = data.Length };
            _memoryCache.Set(key, data, entryOptions);
        }
        catch { }
    }
}
