using GalleryCloud.Api.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace GalleryCloud.Tests;

public class FileWatcherServiceTest : IDisposable
{
    private readonly string _tempDir;
    private readonly string _testImagePath;
    private readonly string _testImageWithExifPath;

    public FileWatcherServiceTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GalleryCloudTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _testImagePath = Path.Combine(_tempDir, "no_exif.jpg");
        _testImageWithExifPath = Path.Combine(_tempDir, "with_exif.jpg");

        // Create a minimal JPEG without EXIF data
        using (var image = new Image<Rgba32>(1, 1))
        {
            // Save without EXIF
            image.Save(_testImagePath, new JpegEncoder { Quality = 70 });
        }

        // Create a JPEG with EXIF DateTimeOriginal
        using (var image = new Image<Rgba32>(1, 1))
        {
            var exifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
            exifProfile.SetValue(
                SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.DateTimeOriginal,
                "2024-01-15T12:00:00");
            image.Metadata.ExifProfile = exifProfile;
            image.Save(_testImageWithExifPath, new JpegEncoder { Quality = 70 });
        }
    }

    [Fact]
    public void Extract_FallbackTime_WhenNoExifDate()
    {
        var fallback = new DateTime(2025, 6, 15, 10, 30, 0);
        var exif = ExifService.Extract(_testImagePath, "ImageSharp", fallback);

        Assert.NotNull(exif.TakenAt);
        Assert.Equal(fallback, exif.TakenAt.Value);
    }

    [Fact]
    public void Extract_NullTakenAt_WhenNoExifDateAndNoFallback()
    {
        var exif = ExifService.Extract(_testImagePath, "ImageSharp");

        Assert.Null(exif.TakenAt);
    }

    [Fact]
    public void Extract_ReturnsDimensions()
    {
        var exif = ExifService.Extract(_testImagePath, "ImageSharp");

        Assert.Equal(1, exif.Width);
        Assert.Equal(1, exif.Height);
    }

    [Fact]
    public void Extract_ReadsExifDate_WhenAvailable()
    {
        var exif = ExifService.Extract(_testImageWithExifPath, "ImageSharp");

        Assert.NotNull(exif.TakenAt);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* */ }
    }
}
