namespace GalleryCloud.Core.Entities;

public class SharePhoto
{
    public string ShareId { get; set; } = string.Empty;
    public string PhotoId { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation
    public Share? Share { get; set; }
    public Photo? Photo { get; set; }
}
