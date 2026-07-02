using System.Data.Common;
using GalleryCloud.Api.Data;
using GalleryCloud.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Tests;

public abstract class ServiceTestBase : IDisposable
{
    private readonly DbConnection _connection;
    protected readonly AppDbContext _db;

    protected ServiceTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        Seed();
    }

    /// <summary>
    /// Override to insert seed data before each test.
    /// </summary>
    protected virtual void Seed() { }

    /// <summary>
    /// Helper: create a user with given username and display name.
    /// </summary>
    protected User CreateUser(string username = "test", string displayName = "Test User",
        string passwordHash = "hashed_password", bool isDeleted = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString("N")[..16],
            Username = username,
            DisplayName = displayName,
            PasswordHash = passwordHash,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    /// <summary>
    /// Helper: create a root for a user.
    /// </summary>
    protected UserRoot CreateRoot(User user, string rootPath = "C:\\Photos",
        bool isEnabled = true, bool isDeleted = false)
    {
        var root = new UserRoot
        {
            Id = Guid.NewGuid().ToString("N")[..16],
            UserId = user.Id,
            RootPath = rootPath,
            IsEnabled = isEnabled,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow
        };
        _db.UserRoots.Add(root);
        _db.SaveChanges();
        return root;
    }

    /// <summary>
    /// Helper: create a photo under a root.
    /// </summary>
    protected Photo CreatePhoto(User user, UserRoot root, string filePath = "photo.jpg",
        string fileFormat = ".jpg", long fileSize = 1024, bool isDeleted = false,
        double? latitude = null, double? longitude = null)
    {
        var photo = new Photo
        {
            Id = Guid.NewGuid().ToString("N")[..16],
            UserId = user.Id,
            RootId = root.Id,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileFormat = fileFormat,
            FileSize = fileSize,
            Orientation = 1,
            IsDeleted = isDeleted,
            Latitude = latitude,
            Longitude = longitude,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Photos.Add(photo);
        _db.SaveChanges();
        return photo;
    }

    /// <summary>
    /// Helper: create a system setting.
    /// </summary>
    protected void CreateSetting(string key, string value)
    {
        _db.SystemSettings.Add(new SystemSetting
        {
            Key = key,
            Value = value,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
    }
}
