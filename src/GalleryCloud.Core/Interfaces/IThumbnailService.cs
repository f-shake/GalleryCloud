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
    Task<Stream?> TryGetCachedAsync(string photoId, ThumbnailSize size, int width);
    void EnqueueAsync(string photoId, ThumbnailSize size, int width);
    Task ConsumeChannelAsync(CancellationToken ct);
    Task<Stream?> GetThumbnailAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct = default);
    Task RegenerateAllAsync(List<string>? sizes = null, CancellationToken ct = default);
    Task FillMissingAsync(List<string>? sizes = null, CancellationToken ct = default);
    void CancelGeneration();
    Task<ThumbnailStats> GetStatsAsync();
    Task<int> ClearCacheAsync();
    ThumbnailGenerationStatus RegenerationStatus { get; }
}
