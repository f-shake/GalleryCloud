namespace GalleryCloud.Core.Settings;

public class ThumbnailOptions
{
    public string Format { get; set; } = "webp";       // jpg / webp / avif
    public int Quality { get; set; } = 80;              // 10-100
    public int ParallelThreads { get; set; } = 2;       // 1 ~ CPU count
    public string CacheDir { get; set; } = "data/thumbnails";
    public int MaxMemoryCacheMb { get; set; } = 512;
}
