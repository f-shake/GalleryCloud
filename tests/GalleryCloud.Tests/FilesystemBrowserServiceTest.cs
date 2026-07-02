using GalleryCloud.Api.Services;
using GalleryCloud.Core.Dtos;

namespace GalleryCloud.Tests;

public class FilesystemBrowserServiceTest
{
    private FilesystemBrowserService CreateService() => new();

    // Note: These tests depend on the actual filesystem.
    // On Windows, C:\ and the temp directory should always exist.

    [Fact]
    public async Task BrowseDirectory_ExistingPath_ReturnsEntries()
    {
        var svc = CreateService();
        var result = await svc.BrowseDirectoryAsync(Path.GetTempPath());
        Assert.NotNull(result);
        Assert.NotEmpty(result.Entries);
        Assert.Equal(Path.GetTempPath().TrimEnd('\\'), result.CurrentPath.TrimEnd('\\'));
    }

    [Fact]
    public async Task BrowseDirectory_NonExistentPath_ReturnsNull()
    {
        var svc = CreateService();
        var result = await svc.BrowseDirectoryAsync(@"C:\ThisPathShouldNotExist_ABCDEF123456");
        Assert.Null(result);
    }

    [Fact]
    public async Task BrowseDirectory_RootLevel_IsRootFlag()
    {
        var svc = CreateService();
        var result = await svc.BrowseDirectoryAsync(@"C:\");
        Assert.NotNull(result);
        Assert.True(result.IsRoot);
    }

    [Fact]
    public async Task BrowseDirectory_EmptyPath_DefaultsToRoot()
    {
        var svc = CreateService();
        var result = await svc.BrowseDirectoryAsync("");
        Assert.NotNull(result);
    }
}
