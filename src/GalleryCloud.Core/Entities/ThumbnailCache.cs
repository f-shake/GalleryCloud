namespace GalleryCloud.Core.Entities;

public class ThumbnailCache
{
    public string PhotoId { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public byte[]? Data { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
