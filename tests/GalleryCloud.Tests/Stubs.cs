using GalleryCloud.Core.Enums;
using GalleryCloud.Core.Interfaces;

namespace GalleryCloud.Tests;

/// <summary>Stub scan service that reports not running.</summary>
public class StubScanService : IScanService
{
    public ScanStatus Status => new();
    public void Cancel() { }
    public Task TriggerFullScanAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    public Task TriggerIncrementalScanAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    public Task TriggerFullScanForAllUsersAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RefreshExifAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Stub thumbnail service that reports not running.</summary>
public class StubThumbnailService : IThumbnailService
{
    public ThumbnailGenerationStatus RegenerationStatus => new();
    public Task<Stream?> TryGetCachedAsync(string photoId, ThumbnailSize size, int width) => Task.FromResult<Stream?>(null);
    public void EnqueueAsync(string photoId, ThumbnailSize size, int width) { }
    public Task ConsumeChannelAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<Stream?> GetThumbnailAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
    public Task RegenerateAllAsync(List<string>? sizes = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task FillMissingAsync(List<string>? sizes = null, CancellationToken ct = default) => Task.CompletedTask;
    public void CancelGeneration() { }
    public Task<ThumbnailStats> GetStatsAsync() => Task.FromResult(new ThumbnailStats());
    public Task<int> ClearCacheAsync() => Task.FromResult(0);
}
