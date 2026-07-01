namespace GalleryCloud.Core.Interfaces;

public record ScanStatus
{
    public bool IsRunning { get; set; }
    public string? Mode { get; set; }
    public string? UserId { get; set; }
    public DateTime? StartedAt { get; set; }
    public int ProcessedFiles { get; set; }
    public int TotalFiles { get; set; }
    public int EstimatedPercent => TotalFiles > 0 ? (int)(100.0 * ProcessedFiles / TotalFiles) : 0;
}

public interface IScanService
{
    ScanStatus Status { get; }
    void Cancel();
    Task TriggerFullScanAsync(string userId, CancellationToken ct = default);
    Task TriggerIncrementalScanAsync(string userId, CancellationToken ct = default);
    Task TriggerFullScanForAllUsersAsync(CancellationToken ct = default);
    Task RefreshExifAsync(string userId, CancellationToken ct = default);
}
