using GalleryCloud.Api.Services;
using GalleryCloud.Core.Dtos;

namespace GalleryCloud.Tests;

public class StatsServiceTest : ServiceTestBase
{
    protected override void Seed()
    {
        var user1 = CreateUser("alice");
        var root1 = CreateRoot(user1, @"C:\Photos");
        for (int i = 0; i < 5; i++)
            CreatePhoto(user1, root1, $"img{i}.jpg", ".jpg", 1024 * 100,
                latitude: i % 2 == 0 ? 39.9 : null,
                longitude: i % 2 == 0 ? 116.4 : null);

        var user2 = CreateUser("bob");
        var root2 = CreateRoot(user2, @"D:\Photos");
        CreatePhoto(user2, root2, "a.heic", ".heic", 2048 * 100);
        CreatePhoto(user2, root2, "b.heic", ".heic", 2048 * 100);
    }

    private StatsService CreateService() => new(_db);

    [Fact]
    public async Task GetAdminStats_ReturnsCorrectCounts()
    {
        var svc = CreateService();
        var stats = await svc.GetAdminStatsAsync();

        Assert.Equal(7, stats.TotalPhotos);
        Assert.Equal(2, stats.TotalUsers);
        Assert.Equal(3, stats.PhotosWithGps);
        Assert.True(stats.TotalSize > 0);
    }

    [Fact]
    public async Task GetAdminStats_FormatDistribution()
    {
        var svc = CreateService();
        var stats = await svc.GetAdminStatsAsync();

        var jpgCount = stats.FormatDistribution.FirstOrDefault(f => f.Format == ".jpg");
        var heicCount = stats.FormatDistribution.FirstOrDefault(f => f.Format == ".heic");

        Assert.Equal(5, jpgCount.Count);
        Assert.Equal(2, heicCount.Count);
    }

    [Fact]
    public async Task GetUserStats_FiltersByUser()
    {
        var svc = CreateService();
        var alice = _db.Users.First(u => u.Username == "alice");

        var stats = await svc.GetUserStatsAsync(alice.Id);

        Assert.Equal(5, stats.TotalPhotos);
        Assert.Equal(1, stats.TotalUsers);
        Assert.Equal(3, stats.PhotosWithGps);
    }
}
