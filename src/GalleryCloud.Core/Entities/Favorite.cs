namespace GalleryCloud.Core.Entities;

public class Favorite
{
    public string UserId { get; set; } = string.Empty;
    public string PhotoId { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public User? User { get; set; }
    public Photo? Photo { get; set; }
}
