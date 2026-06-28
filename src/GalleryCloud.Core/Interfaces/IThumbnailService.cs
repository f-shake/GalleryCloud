using GalleryCloud.Core.Enums;

namespace GalleryCloud.Core.Interfaces;

public record ThumbnailGenerationStatus
{
    public bool IsRunning { get; set; }
    public int Processed { get; set; }
    public int Total { get; set; }
    public int EstimatedPercent => Total > 0 ? (int)(100.0 * Processed / Total) : 0;
}

public record ThumbnailStats
{
    public int TotalPhotos { get; set; }
    public int GridCached { get; set; }
    public int PreviewCached { get; set; }
    public int MissingGrid => Math.Max(0, TotalPhotos - GridCached);
    public int MissingPreview => Math.Max(0, TotalPhotos - PreviewCached);
}

public interface IThumbnailService
{
    /// <summary>Check cache only — returns null if not cached.</summary>
    Task<Stream?> TryGetCachedAsync(string photoId, ThumbnailSize size, int width);
    /// <summary>Enqueue a thumbnail generation request (returns immediately).</summary>
    void EnqueueAsync(string photoId, ThumbnailSize size, int width);
    /// <summary>Consume the background generation channel (called by HostedService).</summary>
    Task ConsumeChannelAsync(CancellationToken ct);
    /// <summary>Full generate with wait (for legacy / existing callers).</summary>
    Task<Stream?> GetThumbnailAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct = default);
    Task RegenerateAllAsync(List<string>? sizes = null, CancellationToken ct = default);
    /// <summary>Generate only missing thumbnails (skip already-cached photos).</summary>
    Task FillMissingAsync(List<string>? sizes = null, CancellationToken ct = default);
    /// <summary>Cancel running regeneration or fill-missing.</summary>
    void CancelGeneration();
    /// <summary>Get thumbnail cache statistics.</summary>
    Task<ThumbnailStats> GetStatsAsync();
    ThumbnailGenerationStatus RegenerationStatus { get; }
}
