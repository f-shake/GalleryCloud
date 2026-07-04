namespace GalleryCloud.Core.Settings;

public static class SettingKeys
{
    // Scan
    public const string ScanCronExpression = "scan.cronExpression";
    public const string ScanSupportedFormats = "scan.supportedFormats";
    public const string ScanExcludePatterns = "scan.excludePatterns";

    // FileWatcher
    public const string FileWatcherEnabled = "filewatcher.enabled";
    public const string FileWatcherDebounceDelayMs = "filewatcher.debounceDelayMs";

    // Thumbnail
    public const string ThumbnailFormat = "thumbnail.format";
    public const string ThumbnailQuality = "thumbnail.quality";
    public const string ThumbnailParallelThreads = "thumbnail.parallelThreads";

    // Preview
    public const string PreviewFormat = "preview.format";
    public const string PreviewQuality = "preview.quality";
    public const string PreviewMaxResolution = "preview.maxResolution";

    // Processing
    public const string ImageProcessingEngine = "image.processingEngine";

    // Map
    public const string MapTileUrlNormal = "map.tileUrlNormal";
    public const string MapTileUrlSatellite = "map.tileUrlSatellite";

    /// <summary>
    /// Normalize a file format string: lowercase, strip leading glob (*), ensure leading dot.
    /// "jpg" → ".jpg", "*.jpg" → ".jpg", ".JPG" → ".jpg", ".jpg" → ".jpg"
    /// </summary>
    public static string NormalizeFormat(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        if (s.StartsWith("*.")) s = s[1..];
        if (!s.StartsWith('.')) s = "." + s;
        return s;
    }
}
