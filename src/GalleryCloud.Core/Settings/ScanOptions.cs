namespace GalleryCloud.Core.Settings;

public class ScanOptions
{
    public string CronExpression { get; set; } = "0 3 * * *";
    public List<string> SupportedFormats { get; set; } = new() { ".jpg", ".jpeg", ".heic", ".avif", ".png", ".webp" };
    public List<string> ExcludePatterns { get; set; } = new() { "**/thumbnails/**", "**/@eaDir/**" };
}
