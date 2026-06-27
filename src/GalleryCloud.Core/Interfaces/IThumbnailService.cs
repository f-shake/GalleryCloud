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
    Task<Stream?> GetThumbnailAsync(string photoId, ThumbnailSize size, int width, CancellationToken ct = default);
    Task RegenerateAllAsync(CancellationToken ct = default);
    ThumbnailGenerationStatus RegenerationStatus { get; }
}
