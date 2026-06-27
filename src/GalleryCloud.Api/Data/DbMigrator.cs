using GalleryCloud.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Data;

public static class DbMigrator
{
    public static async Task MigrateAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Ensure data directory exists
        var dbPath = Path.GetDirectoryName(db.Database.GetConnectionString()?.Replace("Data Source=", ""));
        if (!string.IsNullOrEmpty(dbPath) && !Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath);
        }

        // Apply pending EF Core migrations
        await db.Database.MigrateAsync();

        // Set SQLite pragmas
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA cache_size = -8000;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;");

        // Seed admin account if no users exist
        await SeedAdminAsync(scope.ServiceProvider);
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

        // Ensure admin's photo directory exists
        if (!Directory.Exists(admin.RootPath))
        {
            Directory.CreateDirectory(admin.RootPath);
        }
    }

    private static string HashPassword(string password)
    {
        var salt = "GalleryCloud2026"u8.ToArray();
        using var hmac = new System.Security.Cryptography.HMACSHA256(salt);
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }
}
