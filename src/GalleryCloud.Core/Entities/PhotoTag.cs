namespace GalleryCloud.Core.Entities;

public class PhotoTag
{
    public string PhotoId { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;

    public Photo? Photo { get; set; }
    public Tag? Tag { get; set; }
}
