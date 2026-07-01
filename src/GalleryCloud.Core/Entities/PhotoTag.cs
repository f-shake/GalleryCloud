namespace GalleryCloud.Core.Entities;

public class PhotoTag
{
    public string PhotoId { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Photo? Photo { get; set; }
    public Tag? Tag { get; set; }
}
