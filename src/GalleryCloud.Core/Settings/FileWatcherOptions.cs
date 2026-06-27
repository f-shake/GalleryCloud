namespace GalleryCloud.Core.Settings;

public class FileWatcherOptions
{
    public bool Enabled { get; set; } = true;
    public int DebounceDelayMs { get; set; } = 5000;
}
