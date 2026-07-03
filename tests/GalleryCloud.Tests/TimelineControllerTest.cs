using GalleryCloud.Api.Controllers;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GalleryCloud.Tests;

public class TimelineControllerTest : ServiceTestBase
{
    private readonly UserContext _userCtx;
    private readonly TimelineController _controller;
    private string _userId;

    public TimelineControllerTest()
    {
        _userCtx = new UserContext(_db);
        _userCtx.SetUser(_userId, "alice");
        _controller = new TimelineController(_db, _userCtx);
    }

    protected override void Seed()
    {
        var user = CreateUser("alice");
        _userId = user.Id;
        var root = CreateRoot(user, @"C:\Photos");

        // Day 1: 3 photos on 2026-06-15
        MakePhoto(user, root, "d1_1.jpg", ".jpg", new DateTime(2026, 6, 15, 10, 0, 0));
        MakePhoto(user, root, "d1_2.jpg", ".jpg", new DateTime(2026, 6, 15, 11, 0, 0));
        MakePhoto(user, root, "d1_3.jpg", ".jpg", new DateTime(2026, 6, 15, 12, 0, 0));

        // Day 2: 2 photos on 2026-06-20
        MakePhoto(user, root, "d2_1.jpg", ".jpg", new DateTime(2026, 6, 20, 9, 0, 0));
        MakePhoto(user, root, "d2_2.jpg", ".jpg", new DateTime(2026, 6, 20, 10, 0, 0));

        // Day 3: 1 photo on 2026-07-01 (different month)
        MakePhoto(user, root, "d3_1.jpg", ".jpg", new DateTime(2026, 7, 1, 8, 0, 0));

        // Photo with no date (TakenAt = null)
        MakePhoto(user, root, "nodate.jpg", ".jpg", null);

        // Soft-deleted photo (should be excluded)
        MakePhoto(user, root, "deleted.jpg", ".jpg",
            new DateTime(2025, 1, 1), isDeleted: true);

        // Another user's photo (should be excluded)
        var otherUser = CreateUser("bob");
        var otherRoot = CreateRoot(otherUser, @"D:\Photos");
        MakePhoto(otherUser, otherRoot, "other.jpg", ".jpg", new DateTime(2026, 6, 15));
    }

    private Photo MakePhoto(User user, UserRoot root, string filePath, string fileFormat,
        DateTime? takenAt, bool isDeleted = false)
    {
        var photo = new Photo
        {
            Id = Guid.NewGuid().ToString("N")[..16],
            UserId = user.Id,
            RootId = root.Id,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileFormat = fileFormat,
            FileSize = 1024,
            Orientation = 1,
            TakenAt = takenAt,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Photos.Add(photo);
        _db.SaveChanges();
        return photo;
    }

    [Fact]
    public async Task GetDailyDensity_DefaultAsc_ReturnsChronologicalOrder()
    {
        var result = await _controller.GetDailyDensity("asc");
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<List<DailyDensityItem>>(ok.Value);

        Assert.Equal(3, data.Count);
        Assert.Equal("2026-06-15", data[0].Date);
        Assert.Equal(3, data[0].Count);
        Assert.Equal("2026-06-20", data[1].Date);
        Assert.Equal(2, data[1].Count);
        Assert.Equal("2026-07-01", data[2].Date);
        Assert.Equal(1, data[2].Count);
    }

    [Fact]
    public async Task GetDailyDensity_Desc_ReturnsReverseOrder()
    {
        var result = await _controller.GetDailyDensity("desc");
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<List<DailyDensityItem>>(ok.Value);

        Assert.Equal(3, data.Count);
        Assert.Equal("2026-07-01", data[0].Date);
        Assert.Equal(1, data[0].Count);
        Assert.Equal("2026-06-20", data[1].Date);
        Assert.Equal(2, data[1].Count);
        Assert.Equal("2026-06-15", data[2].Date);
        Assert.Equal(3, data[2].Count);
    }

    [Fact]
    public async Task GetDailyDensity_ExcludesSoftDeletedAndOtherUsers()
    {
        // Should only have 3 days (excludes deleted + other user's photos)
        var result = await _controller.GetDailyDensity("asc");
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<List<DailyDensityItem>>(ok.Value);

        Assert.DoesNotContain(data, d => d.Date == "2025-01-01");
        Assert.Equal(3, data.Count);
    }

    [Fact]
    public async Task GetDateIds_ReturnsIdsForDate()
    {
        var result = await _controller.GetDateIds("2026-06-15");
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<DateIdsResponse>(ok.Value);

        Assert.Equal("2026-06-15", data.Date);
        Assert.Equal(3, data.Items.Count);
        Assert.All(data.Items, i => Assert.NotNull(i.DateInt));
    }

    [Fact]
    public async Task GetDateIds_NoPhotosForDate_ReturnsEmpty()
    {
        var result = await _controller.GetDateIds("2025-01-02");
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<DateIdsResponse>(ok.Value);

        Assert.Equal("2025-01-02", data.Date);
        Assert.Empty(data.Items);
    }

    [Fact]
    public async Task GetDateIds_InvalidDate_ReturnsBadRequest()
    {
        var result = await _controller.GetDateIds("not-a-date");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetDateIds_ReturnsNewestFirst()
    {
        var result = await _controller.GetDateIds("2026-06-15");
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<DateIdsResponse>(ok.Value);

        // Photos taken at 12:00, 11:00, 10:00 — newest first
        var photos = _db.Photos.Where(p => data.Items.Select(i => i.Id).Contains(p.Id))
            .OrderByDescending(p => p.TakenAt).ToList();
        Assert.Equal(data.Items[0].Id, photos[0].Id);
        Assert.Equal(data.Items[2].Id, photos[2].Id);
    }

    [Fact]
    public async Task GetNullDateIds_ReturnsPhotosWithoutTakenAt()
    {
        var result = await _controller.GetNullDateIds();
        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<List<string>>(ok.Value);

        Assert.Single(data);
    }

    [Fact]
    public async Task Unauthenticated_ReturnsUnauthorized()
    {
        _userCtx.Clear();

        var r1 = await _controller.GetDailyDensity();
        Assert.IsType<UnauthorizedResult>(r1);

        var r2 = await _controller.GetDateIds("2026-06-15");
        Assert.IsType<UnauthorizedResult>(r2);

        var r3 = await _controller.GetNullDateIds();
        Assert.IsType<UnauthorizedResult>(r3);
    }
}
