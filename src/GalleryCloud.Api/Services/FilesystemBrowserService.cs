using GalleryCloud.Core.Dtos;
using GalleryCloud.Core.Interfaces;

namespace GalleryCloud.Api.Services;

public class FilesystemBrowserService : IFilesystemBrowserService
{
    public Task<List<FsEntryDto>> GetDrivesAsync()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FsEntryDto(
                d.Name.TrimEnd(Path.DirectorySeparatorChar) + "/",
                d.RootDirectory.FullName,
                true
            ))
            .ToList();

        if (drives.Count == 0)
            drives = new List<FsEntryDto> { new("/", "/", true) };

        return Task.FromResult(drives);
    }

    public Task<FsBrowseResult?> BrowseDirectoryAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = "/";

        var normalized = path.Replace('/', Path.DirectorySeparatorChar);

        if (!Directory.Exists(normalized))
            return Task.FromResult<FsBrowseResult?>(null);

        var dirInfo = new DirectoryInfo(normalized);

        var entries = dirInfo.EnumerateDirectories()
            .Where(d => !d.Name.StartsWith('.'))
            .Select(d => new FsEntryDto(d.Name, d.FullName, false))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parent = dirInfo.Parent?.FullName;
        var isRoot = false;
        try
        {
            var driveRoot = Path.GetPathRoot(normalized);
            isRoot = string.Equals(normalized.TrimEnd(Path.DirectorySeparatorChar),
                driveRoot?.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        return Task.FromResult<FsBrowseResult?>(new FsBrowseResult(dirInfo.FullName, entries, parent, isRoot));
    }
}
