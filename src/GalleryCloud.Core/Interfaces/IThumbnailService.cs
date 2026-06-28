using GalleryCloud.Core.Enums;

namespace GalleryCloud.Core.Interfaces;

public record ThumbnailGenerationStatus
{
    public bool IsRunning { get; set; }
    public int Processed { get; set; }
    public int Total { get; set; }
    public int EstimatedPercent => Total > 0 ? (int)(100.0 * Processed / Total) : 0;
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
    Task RegenerateAllAsync(CancellationToken ct = default);
    ThumbnailGenerationStatus RegenerationStatus { get; }
}
