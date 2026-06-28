using GalleryCloud.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Data;

public static class DbMigrator
{
    public static async Task MigrateAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var mainDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();

        await MigrateDbAsync(mainDb, -8000);
        await MigrateDbAsync(thumbDb, -64000);

        await SeedAdminAsync(scope.ServiceProvider);
    }

    private static async Task MigrateDbAsync(DbContext db, int cacheSizeKb)
    {
        var connStr = db.Database.GetConnectionString();
        var dbPath = connStr?.Replace("Data Source=", "").Trim();
        if (!string.IsNullOrEmpty(dbPath))
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        await db.Database.MigrateAsync();

        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;");
        await db.Database.ExecuteSqlRawAsync($"PRAGMA cache_size = {cacheSizeKb};");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;");
    }

    private static async Task SeedAdminAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();

        if (await db.Users.AnyAsync())
            return;

        var admin = new User
        {
            Id = "admin0000000000000000000000000", // fixed predictable ID, 31 chars
            Username = "admin",
            PasswordHash = HashPassword("admin"),
            DisplayName = "Administrator",
            RootPath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "photos"),
            IsAdmin = true,
            IsActive = true,
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync();

        if (!Directory.Exists(admin.RootPath))
            Directory.CreateDirectory(admin.RootPath);
    }

    private static string HashPassword(string password)
    {
        var salt = "GalleryCloud2026"u8.ToArray();
        using var hmac = new System.Security.Cryptography.HMACSHA256(salt);
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }
}
