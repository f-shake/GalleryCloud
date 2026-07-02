using GalleryCloud.Core.Dtos;

namespace GalleryCloud.Core.Interfaces;

public interface IFilesystemBrowserService
{
    Task<List<FsEntryDto>> GetDrivesAsync();
    Task<FsBrowseResult?> BrowseDirectoryAsync(string path);
}
