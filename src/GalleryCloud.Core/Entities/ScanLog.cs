namespace GalleryCloud.Core.Entities;

public class ScanLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public int TotalFound { get; set; }
    public int NewAdded { get; set; }
    public int SoftDeleted { get; set; }
    public string Mode { get; set; } = "full";

    public User? User { get; set; }
}
