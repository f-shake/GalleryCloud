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
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();
        var sizeKey = size.ToString().ToLowerInvariant();

        var cacheRecord = await thumbDb.ThumbnailCaches
            .FirstOrDefaultAsync(t => t.PhotoId == photoId && t.Size == sizeKey);

        if (cacheRecord?.Data == null || cacheRecord.Data.Length == 0)
            return null;

        CacheInMemory(memKey, cacheRecord.Data);
        return new MemoryStream(cacheRecord.Data);
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
        Interlocked.Increment(ref _inProgressCount);
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
        var cached = await TryGetCachedAsync(photoId, size, width);
        if (cached != null) return cached;

        return await InternalGenerateAsync(photoId, size, width, ct);
    }

    private async Task<Stream?> InternalGenerateAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();
        var sizeKey = size.ToString().ToLowerInvariant();
        var isPreview = sizeKey == "preview";

        // Photo + user (for source file path only)
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted, ct);
        if (photo == null) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == photo.UserId, ct);
        if (user == null) return null;

        var fullPath = Path.GetFullPath(Path.Combine(user.RootPath, photo.FilePath));
        if (!File.Exists(fullPath)) return null;

        var quality = await _settings.GetAsync(
            isPreview ? SettingKeys.PreviewQuality : SettingKeys.ThumbnailQuality,
            isPreview ? 70 : 60);
        var fmt = await _settings.GetAsync(isPreview ? SettingKeys.PreviewFormat : SettingKeys.ThumbnailFormat, "jpeg");

        // Decode + process
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var image = await Image.LoadAsync(fullPath, ct);
        var decodeMs = sw.ElapsedMilliseconds;
        int srcW = image.Width, srcH = image.Height;

        image.Mutate(x => x.AutoOrient());

        bool isWebp = fmt.Equals("webp", StringComparison.OrdinalIgnoreCase);
        IImageEncoder encoder = isWebp
            ? new WebpEncoder { Quality = quality }
            : new JpegEncoder { Quality = quality };
        string format = fmt.ToLowerInvariant();

        if (isPreview)
        {
            var maxRes = await _settings.GetAsync(SettingKeys.PreviewMaxResolution, 5000);
            var longerSide = Math.Max(image.Width, image.Height);
            if (longerSide > maxRes)
            {
                var scale = (double)maxRes / longerSide;
                image.Mutate(x => x.Resize((int)(image.Width * scale), (int)(image.Height * scale)));
            }
        }
        else
        {
            var targetW = Math.Min(width, image.Width);
            image.Mutate(x => x.Resize(targetW, 0));
        }

        // Encode to memory
        sw.Restart();
        using var ms = new MemoryStream();
        await image.SaveAsync(ms, encoder, ct);
        var encodeMs = sw.ElapsedMilliseconds;
        var resultBytes = ms.ToArray();

        // Store in thumbnail DB
        sw.Restart();
        var record = await thumbDb.ThumbnailCaches
            .FirstOrDefaultAsync(t => t.PhotoId == photoId && t.Size == sizeKey, ct);
        if (record == null)
        {
            record = new ThumbnailCache
            {
                PhotoId = photoId,
                Size = sizeKey,
                Format = format,
                Data = resultBytes
            };
            thumbDb.ThumbnailCaches.Add(record);
        }
        else
        {
            record.Data = resultBytes;
            record.Format = format;
            record.CreatedAt = DateTime.UtcNow;
        }

        await thumbDb.SaveChangesAsync(ct);
        var dbMs = sw.ElapsedMilliseconds;

        CacheInMemory($"thumb:{photoId}:{sizeKey}", resultBytes);
        var remaining = Interlocked.Decrement(ref _queueCount);
        _logger.LogInformation("缩略图完成 [{Size}] {PhotoId} 原图{W}x{H} 解码:{Decode}ms 编码:{Encode}ms DB:{Db}ms 并行:{Parallel}",
            sizeKey, photoId[..8], srcW, srcH, decodeMs, encodeMs, dbMs, Math.Max(0, _inProgressCount));

        return new MemoryStream(resultBytes);
    }

    // ── Cancel ──────────────────────────────────────────────────
    public void CancelGeneration()
    {
        _regenerationCts?.Cancel();
    }

    // ── Stats ───────────────────────────────────────────────────
    public async Task<ThumbnailStats> GetStatsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();

        var totalPhotos = await db.Photos.CountAsync(p => !p.IsDeleted);
        var gridCached = await thumbDb.ThumbnailCaches.CountAsync(t => t.Size == "grid");
        var previewCached = await thumbDb.ThumbnailCaches.CountAsync(t => t.Size == "preview");

        return new ThumbnailStats
        {
            TotalPhotos = totalPhotos,
            GridCached = gridCached,
            PreviewCached = previewCached,
        };
    }

    // ── Fill missing ────────────────────────────────────────────
    public async Task FillMissingAsync(List<string>? sizes = null, CancellationToken ct = default)
    {
        if (RegenerationStatus.IsRunning) return;

        _regenerationCts?.Cancel();
        _regenerationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var allSizes = new[] { ("grid", ThumbnailSize.Grid, 400), ("preview", ThumbnailSize.Preview, 0) };
        var selected = sizes is { Count: > 0 }
            ? allSizes.Where(s => sizes.Contains(s.Item1, StringComparer.OrdinalIgnoreCase)).ToArray()
            : allSizes;
        if (selected.Length == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();

        var photoIds = await db.Photos.Where(p => !p.IsDeleted).Select(p => p.Id).ToListAsync(_regenerationCts.Token);
        var cachedKeys = await thumbDb.ThumbnailCaches
            .Select(t => new { t.PhotoId, t.Size })
            .ToListAsync(_regenerationCts.Token);
        var cachedSet = new HashSet<string>(cachedKeys.Select(c => $"{c.PhotoId}:{c.Size}"));

        var missing = new List<(string PhotoId, ThumbnailSize Size, int Width)>();
        foreach (var pid in photoIds)
        {
            foreach (var (key, sz, w) in selected)
            {
                if (!cachedSet.Contains($"{pid}:{key}"))
                    missing.Add((pid, sz, w));
            }
        }

        if (missing.Count == 0)
        {
            _regenerationCts.Dispose();
            _regenerationCts = null;
            RegenerationStatus = new ThumbnailGenerationStatus();
            return;
        }

        RegenerationStatus = new ThumbnailGenerationStatus { IsRunning = true, Total = missing.Count, Processed = 0 };
        var parallel = await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 4);
        var semaphore = new SemaphoreSlim(Math.Max(1, parallel));

        var tasks = missing.Select(async item =>
        {
            var (pid, sz, w) = item;
            try { await semaphore.WaitAsync(_regenerationCts.Token); }
            catch { return; }
            try
            {
                if (_regenerationCts.Token.IsCancellationRequested) return;
                Interlocked.Increment(ref _inProgressCount);
                try { await InternalGenerateAsync(pid, sz, w, _regenerationCts.Token); }
                catch { }
                finally { Interlocked.Decrement(ref _inProgressCount); }
                RegenerationStatus = RegenerationStatus with { Processed = RegenerationStatus.Processed + 1 };
            }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(tasks);

        RegenerationStatus = new ThumbnailGenerationStatus();
    }

    // ── Regenerate all ─────────────────────────────────────────
    public async Task RegenerateAllAsync(List<string>? sizes = null, CancellationToken ct = default)
    {
        if (RegenerationStatus.IsRunning) return;

        _regenerationCts?.Cancel();
        _regenerationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var allSizes = new[] { (ThumbnailSize.Grid, 400, "grid"), (ThumbnailSize.Preview, 0, "preview") };
        var selected = sizes is { Count: > 0 }
            ? allSizes.Where(s => sizes.Contains(s.Item3, StringComparer.OrdinalIgnoreCase)).ToArray()
            : allSizes;
        if (selected.Length == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();

        // Clear cache records for selected sizes
        foreach (var (_, _, sizeKey) in selected)
        {
            var toRemove = await thumbDb.ThumbnailCaches.Where(t => t.Size == sizeKey).ToListAsync(_regenerationCts.Token);
            thumbDb.ThumbnailCaches.RemoveRange(toRemove);
        }
        await thumbDb.SaveChangesAsync(_regenerationCts.Token);

        var photoIds = await db.Photos.Where(p => !p.IsDeleted).Select(p => p.Id).ToListAsync(_regenerationCts.Token);
        var workItems = new List<(string PhotoId, ThumbnailSize Size, int Width)>();
        foreach (var pid in photoIds)
            foreach (var (sz, w, _) in selected)
                workItems.Add((pid, sz, w));

        RegenerationStatus = new ThumbnailGenerationStatus { IsRunning = true, Total = workItems.Count, Processed = 0 };
        var parallel = await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 4);
        var semaphore = new SemaphoreSlim(Math.Max(1, parallel));

        var tasks = workItems.Select(async item =>
        {
            var (pid, sz, w) = item;
            try { await semaphore.WaitAsync(_regenerationCts.Token); }
            catch { return; }
            try
            {
                if (_regenerationCts.Token.IsCancellationRequested) return;
                Interlocked.Increment(ref _inProgressCount);
                try { await InternalGenerateAsync(pid, sz, w, _regenerationCts.Token); }
                catch { }
                finally { Interlocked.Decrement(ref _inProgressCount); }
                RegenerationStatus = RegenerationStatus with { Processed = RegenerationStatus.Processed + 1 };
            }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(tasks);

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
