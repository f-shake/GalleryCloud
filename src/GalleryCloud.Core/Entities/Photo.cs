namespace GalleryCloud.Core.Entities;

public class Photo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileFormat { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int Orientation { get; set; } = 1;
    public DateTime? TakenAt { get; set; }
    public string? DeviceModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Md5Hash { get; set; }
    public DateTime? FileModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
}
