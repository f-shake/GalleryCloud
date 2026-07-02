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
    public const string ThumbnailMaxMemoryCacheMb = "thumbnail.maxMemoryCacheMb";

    // Preview
    public const string PreviewFormat = "preview.format";
    public const string PreviewQuality = "preview.quality";
    public const string PreviewMaxResolution = "preview.maxResolution";

    // Processing
    public const string ImageProcessingEngine = "image.processingEngine";

    // Map
    public const string MapTileUrlNormal = "map.tileUrlNormal";
    public const string MapTileUrlSatellite = "map.tileUrlSatellite";
}
