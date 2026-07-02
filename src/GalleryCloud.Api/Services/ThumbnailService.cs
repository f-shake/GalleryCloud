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
using ImageMagick;

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

    private readonly object _regLock = new();
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
        var inflight = new List<Task>(32);
        var currentParallel = Math.Max(1, await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 4));

        await foreach (var (photoId, size, width) in _channel.Reader.ReadAllAsync(ct))
        {
            // Re-read setting each iteration so changes take effect without restart
            currentParallel = Math.Max(1, await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 4));

            // Remove completed tasks
            inflight.RemoveAll(t => t.IsCompleted);

            // Wait if at capacity
            while (inflight.Count >= currentParallel)
            {
                var done = await Task.WhenAny(inflight);
                inflight.Remove(done);
            }

            // EnqueueAsync already handles dedup via _queuedIds — just process
            var task = Task.Run(() => ProcessChannelItemAsync(photoId, size, width, ct), ct);
            inflight.Add(task);
        }

        await Task.WhenAll(inflight);
    }

    private async Task ProcessChannelItemAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct)
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

        // Photo + root (for source file path)
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted, ct);
        if (photo == null) return null;

        var root = await db.UserRoots.FirstOrDefaultAsync(r => r.Id == photo.RootId, ct);
        if (root == null) return null;

        var fullPath = Path.GetFullPath(Path.Combine(root.RootPath, photo.FilePath));
        if (!File.Exists(fullPath)) return null;

        var quality = await _settings.GetAsync(
            isPreview ? SettingKeys.PreviewQuality : SettingKeys.ThumbnailQuality,
            isPreview ? 70 : 60);
        var fmt = await _settings.GetAsync(isPreview ? SettingKeys.PreviewFormat : SettingKeys.ThumbnailFormat, "jpeg");

        // Select processing engine
        var engine = await _settings.GetAsync(SettingKeys.ImageProcessingEngine, "ImageSharp");

        // Decode + process
        var sw = System.Diagnostics.Stopwatch.StartNew();
        byte[] resultBytes;
        int srcW, srcH;
        long decodeMs, encodeMs;
        string format = fmt.ToLowerInvariant();

        if (engine.Equals("MagickNET", StringComparison.OrdinalIgnoreCase))
        {
            (resultBytes, srcW, srcH, decodeMs, encodeMs) = await ProcessWithMagickNetAsync(
                fullPath, isPreview, quality, fmt, width, ct);
        }
        else
        {
            using var image = await Image.LoadAsync(fullPath, ct);
            decodeMs = sw.ElapsedMilliseconds;
            srcW = image.Width;
            srcH = image.Height;

            image.Mutate(x => x.AutoOrient());

            bool isWebp = fmt.Equals("webp", StringComparison.OrdinalIgnoreCase);
            IImageEncoder encoder = isWebp
                ? new WebpEncoder { Quality = quality }
                : new JpegEncoder { Quality = quality };

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
            encodeMs = sw.ElapsedMilliseconds;
            resultBytes = ms.ToArray();
        }

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
        _logger.LogInformation("已生成缩略图 [{Size}] {PhotoId} 原图{W}x{H} 解码:{Decode}ms 编码:{Encode}ms DB:{Db}ms 排队:{Queue} 并行:{Parallel}",
            sizeKey switch { "preview" => "pre", "grid" => "grd", _ => sizeKey }, photoId[..8], srcW, srcH, decodeMs, encodeMs, dbMs, Math.Max(0, remaining), Math.Max(0, _inProgressCount));

        return new MemoryStream(resultBytes);
    }

    // ── Batch generate: one decode, multiple sizes ───────────────

    private async Task InternalGenerateBatchAsync(string photoId,
        List<(ThumbnailSize Size, int Width)> sizes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();

        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted, ct);
        if (photo == null) return;
        var root = await db.UserRoots.FirstOrDefaultAsync(r => r.Id == photo.RootId, ct);
        if (root == null) return;
        var fullPath = Path.GetFullPath(Path.Combine(root.RootPath, photo.FilePath));
        if (!File.Exists(fullPath)) return;

        // Read all settings upfront
        var engine = await _settings.GetAsync(SettingKeys.ImageProcessingEngine, "ImageSharp");
        var maxRes = await _settings.GetAsync(SettingKeys.PreviewMaxResolution, 5000);
        var gridQuality = await _settings.GetAsync(SettingKeys.ThumbnailQuality, 60);
        var previewQuality = await _settings.GetAsync(SettingKeys.PreviewQuality, 70);
        var gridFmt = (await _settings.GetAsync(SettingKeys.ThumbnailFormat, "jpeg")).ToLowerInvariant();
        var previewFmt = (await _settings.GetAsync(SettingKeys.PreviewFormat, "jpeg")).ToLowerInvariant();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (engine.Equals("MagickNET", StringComparison.OrdinalIgnoreCase))
        {
            using var image = new MagickImage(fullPath);
            long decodeMs = sw.ElapsedMilliseconds;
            var srcW = (int)image.Width;
            var srcH = (int)image.Height;
            image.AutoOrient();

            foreach (var (size, width) in sizes)
            {
                var isPreview = size == ThumbnailSize.Preview;
                var sizeKey = size.ToString().ToLowerInvariant();

                using var cloned = image.Clone();
                if (isPreview)
                {
                    var longerSide = Math.Max((int)cloned.Width, (int)cloned.Height);
                    if (longerSide > maxRes)
                    {
                        var scale = (double)maxRes / longerSide;
                        cloned.Resize(new MagickGeometry((uint)(cloned.Width * scale), (uint)(cloned.Height * scale)) { IgnoreAspectRatio = false });
                    }
                }
                else
                {
                    var targetW = Math.Min((uint)width, cloned.Width);
                    cloned.Resize(new MagickGeometry(targetW, 0) { IgnoreAspectRatio = false });
                }

                var quality = isPreview ? previewQuality : gridQuality;
                cloned.Quality = (uint)quality;
                var fmt = isPreview ? previewFmt : gridFmt;
                var format = fmt == "webp" ? MagickFormat.WebP : MagickFormat.Jpeg;

                sw.Restart();
                using var ms = new MemoryStream();
                await cloned.WriteAsync(ms, format, ct);
                var encodeMs = sw.ElapsedMilliseconds;
                var resultBytes = ms.ToArray();

                sw.Restart();
                await SaveCacheRecordAsync(thumbDb, photoId, sizeKey, fmt, resultBytes, ct);
                var dbMs = sw.ElapsedMilliseconds;

                CacheInMemory($"thumb:{photoId}:{sizeKey}", resultBytes);
                _logger.LogInformation("已生成缩略图 [{Size}] {PhotoId} 原图{W}x{H} 解码:{Decode}ms 编码:{Encode}ms DB:{Db}ms",
                    sizeKey switch { "preview" => "pre", "grid" => "grd", _ => sizeKey }, photoId[..8], srcW, srcH, decodeMs, encodeMs, dbMs);
            }
        }
        else
        {
            using var image = await Image.LoadAsync(fullPath, ct);
            long decodeMs = sw.ElapsedMilliseconds;
            var srcW = image.Width;
            var srcH = image.Height;
            image.Mutate(x => x.AutoOrient());

            foreach (var (size, width) in sizes)
            {
                var isPreview = size == ThumbnailSize.Preview;
                var sizeKey = size.ToString().ToLowerInvariant();
                var quality = isPreview ? previewQuality : gridQuality;
                var fmt = isPreview ? previewFmt : gridFmt;
                var isWebp = fmt == "webp";
                IImageEncoder encoder = isWebp ? new WebpEncoder { Quality = quality } : new JpegEncoder { Quality = quality };

                sw.Restart();
                using var processed = image.Clone(ctx =>
                {
                    if (isPreview)
                    {
                        var longerSide = Math.Max(ctx.GetCurrentSize().Width, ctx.GetCurrentSize().Height);
                        if (longerSide > maxRes)
                        {
                            var scale = (double)maxRes / longerSide;
                            ctx.Resize((int)(ctx.GetCurrentSize().Width * scale), (int)(ctx.GetCurrentSize().Height * scale));
                        }
                    }
                    else
                    {
                        ctx.Resize(Math.Min(width, ctx.GetCurrentSize().Width), 0);
                    }
                });

                using var ms = new MemoryStream();
                await processed.SaveAsync(ms, encoder, ct);
                var encodeMs = sw.ElapsedMilliseconds;
                var resultBytes = ms.ToArray();

                sw.Restart();
                await SaveCacheRecordAsync(thumbDb, photoId, sizeKey, fmt, resultBytes, ct);
                var dbMs = sw.ElapsedMilliseconds;

                CacheInMemory($"thumb:{photoId}:{sizeKey}", resultBytes);
                _logger.LogInformation("已生成缩略图 [{Size}] {PhotoId} 原图{W}x{H} 解码:{Decode}ms 编码:{Encode}ms DB:{Db}ms",
                    sizeKey switch { "preview" => "pre", "grid" => "grd", _ => sizeKey }, photoId[..8], srcW, srcH, decodeMs, encodeMs, dbMs);
            }
        }
    }

    private async Task SaveCacheRecordAsync(ThumbnailDbContext thumbDb, string photoId, string sizeKey, string format, byte[] data, CancellationToken ct)
    {
        var record = await thumbDb.ThumbnailCaches
            .FirstOrDefaultAsync(t => t.PhotoId == photoId && t.Size == sizeKey, ct);
        if (record == null)
        {
            record = new ThumbnailCache { PhotoId = photoId, Size = sizeKey, Format = format, Data = data };
            thumbDb.ThumbnailCaches.Add(record);
        }
        else
        {
            record.Data = data;
            record.Format = format;
            record.CreatedAt = DateTime.UtcNow;
        }
        await thumbDb.SaveChangesAsync(ct);
    }

    // ── Magick.NET processing ──────────────────────────────────────

    private async Task<(byte[] Data, int Width, int Height, long DecodeMs, long EncodeMs)> ProcessWithMagickNetAsync(
        string fullPath, bool isPreview, int quality, string fmt, int width, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var image = new MagickImage(fullPath);
        ct.ThrowIfCancellationRequested();

        int srcW = (int)image.Width, srcH = (int)image.Height;
        image.AutoOrient();

        if (isPreview)
        {
            var maxRes = await _settings.GetAsync(SettingKeys.PreviewMaxResolution, 5000);
            var longerSide = Math.Max((int)image.Width, (int)image.Height);
            if (longerSide > maxRes)
            {
                var scale = (double)maxRes / longerSide;
                image.Resize(new MagickGeometry((uint)(image.Width * scale), (uint)(image.Height * scale))
                    { IgnoreAspectRatio = false });
            }
        }
        else
        {
            var targetW = Math.Min((uint)width, image.Width);
            image.Resize(new MagickGeometry(targetW, 0) { IgnoreAspectRatio = false });
        }

        image.Quality = (uint)quality;
        var format = fmt.Equals("webp", StringComparison.OrdinalIgnoreCase)
            ? MagickFormat.WebP
            : MagickFormat.Jpeg;

        // Now measure encode separately — decode includes lazy pixel decode triggered above
        var decodeMs = sw.ElapsedMilliseconds;
        sw.Restart();
        using var ms = new MemoryStream();
        await image.WriteAsync(ms, format, ct);
        var encodeMs = sw.ElapsedMilliseconds;

        return (ms.ToArray(), srcW, srcH, decodeMs, encodeMs);
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

        var activeRootIds = await db.UserRoots
            .Where(r => r.IsEnabled)
            .Select(r => r.Id)
            .ToListAsync();

        var totalPhotos = await db.Photos
            .CountAsync(p => !p.IsDeleted && activeRootIds.Contains(p.RootId));

        var validPhotoIds = await db.Photos
            .Where(p => !p.IsDeleted && activeRootIds.Contains(p.RootId))
            .Select(p => p.Id)
            .ToListAsync();

        var gridCached = await thumbDb.ThumbnailCaches
            .CountAsync(t => t.Size == "grid" && validPhotoIds.Contains(t.PhotoId));
        var previewCached = await thumbDb.ThumbnailCaches
            .CountAsync(t => t.Size == "preview" && validPhotoIds.Contains(t.PhotoId));

        return new ThumbnailStats
        {
            TotalPhotos = totalPhotos,
            GridCached = gridCached,
            PreviewCached = previewCached,
        };
    }

    public async Task<int> ClearCacheAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();
        var count = await thumbDb.ThumbnailCaches.CountAsync();
        thumbDb.ThumbnailCaches.RemoveRange(thumbDb.ThumbnailCaches);
        await thumbDb.SaveChangesAsync();
        return count;
    }

    // ── Fill missing ────────────────────────────────────────────
    public async Task FillMissingAsync(List<string>? sizes = null, CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        lock (_regLock)
        {
            if (RegenerationStatus.IsRunning) return;
            _regenerationCts?.Cancel();
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _regenerationCts = cts;
        }

        var allSizes = new[] { ("grid", ThumbnailSize.Grid, 400), ("preview", ThumbnailSize.Preview, 0) };
        var selected = sizes is { Count: > 0 }
            ? allSizes.Where(s => sizes.Contains(s.Item1, StringComparer.OrdinalIgnoreCase)).ToArray()
            : allSizes;
        if (selected.Length == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();

        var activeRootIds = await db.UserRoots.Where(r => r.IsEnabled).Select(r => r.Id).ToListAsync(cts.Token);
        var photoIds = await db.Photos
            .Where(p => !p.IsDeleted && activeRootIds.Contains(p.RootId))
            .Select(p => p.Id).ToListAsync(cts.Token);
        var cachedKeys = await thumbDb.ThumbnailCaches
            .Select(t => new { t.PhotoId, t.Size })
            .ToListAsync(cts.Token);
        var cachedSet = new HashSet<string>(cachedKeys.Select(c => $"{c.PhotoId}:{c.Size}"));

        // Group by photo so each photo decodes only once
        var missing = new List<(string PhotoId, List<(ThumbnailSize Size, int Width)> Sizes)>();
        int totalSizeCount = 0;
        foreach (var pid in photoIds)
        {
            var needed = new List<(ThumbnailSize Size, int Width)>();
            foreach (var (key, sz, w) in selected)
            {
                if (!cachedSet.Contains($"{pid}:{key}"))
                {
                    needed.Add((sz, w));
                    totalSizeCount++;
                }
            }
            if (needed.Count > 0)
                missing.Add((pid, needed));
        }

        if (missing.Count == 0)
        {
            cts.Dispose();
            lock (_regLock) { _regenerationCts = null; RegenerationStatus = new(); }
            return;
        }

        lock (_regLock) { RegenerationStatus = new ThumbnailGenerationStatus { IsRunning = true, Total = totalSizeCount, Processed = 0 }; }
        var parallel = await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 4);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, parallel),
            CancellationToken = cts.Token
        };

        try
        {
            await Parallel.ForEachAsync(missing, parallelOptions, async (item, ct) =>
            {
                var (pid, sizes) = item;
                try
                {
                    Interlocked.Increment(ref _inProgressCount);
                    try { await InternalGenerateBatchAsync(pid, sizes, ct); }
                    catch { }
                    finally { Interlocked.Decrement(ref _inProgressCount); }
                    RegenerationStatus = RegenerationStatus with { Processed = RegenerationStatus.Processed + sizes.Count };
                }
                catch (OperationCanceledException) { }
            });
        }
        catch (OperationCanceledException) { }

        lock (_regLock) { RegenerationStatus = new(); }
    }

    // ── Regenerate all ─────────────────────────────────────────
    public async Task RegenerateAllAsync(List<string>? sizes = null, CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        lock (_regLock)
        {
            if (RegenerationStatus.IsRunning) return;
            _regenerationCts?.Cancel();
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _regenerationCts = cts;
        }

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
            var toRemove = await thumbDb.ThumbnailCaches.Where(t => t.Size == sizeKey).ToListAsync(cts.Token);
            thumbDb.ThumbnailCaches.RemoveRange(toRemove);
        }
        await thumbDb.SaveChangesAsync(cts.Token);

        var activeRootIds = await db.UserRoots.Where(r => r.IsEnabled).Select(r => r.Id).ToListAsync(cts.Token);
        var photoIds = await db.Photos
            .Where(p => !p.IsDeleted && activeRootIds.Contains(p.RootId))
            .Select(p => p.Id).ToListAsync(cts.Token);

        // Group by photo so each photo decodes only once
        var workItems = photoIds.Select(pid => (
            PhotoId: pid,
            Sizes: selected.Select(s => (s.Item1, s.Item2)).ToList()
        )).ToList();
        int totalSizeCount = workItems.Sum(w => w.Sizes.Count);

        lock (_regLock) { RegenerationStatus = new ThumbnailGenerationStatus { IsRunning = true, Total = totalSizeCount, Processed = 0 }; }
        var parallel = await _settings.GetAsync(SettingKeys.ThumbnailParallelThreads, 4);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, parallel),
            CancellationToken = cts.Token
        };

        try
        {
            await Parallel.ForEachAsync(workItems, parallelOptions, async (item, ct) =>
            {
                var (pid, sizes) = item;
                try
                {
                    Interlocked.Increment(ref _inProgressCount);
                    try { await InternalGenerateBatchAsync(pid, sizes, ct); }
                    catch { }
                    finally { Interlocked.Decrement(ref _inProgressCount); }
                    RegenerationStatus = RegenerationStatus with { Processed = RegenerationStatus.Processed + sizes.Count };
                }
                catch (OperationCanceledException) { }
            });
        }
        catch (OperationCanceledException) { }

        lock (_regLock) { RegenerationStatus = new(); }
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
