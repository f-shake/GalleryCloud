using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Data;

public static class DbMigrator
{
    public static async Task MigrateAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var mainDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var thumbDb = scope.ServiceProvider.GetRequiredService<ThumbnailDbContext>();

        await InitDbAsync(mainDb, -8000);
        await InitDbAsync(thumbDb, -64000);
    }

    private static async Task InitDbAsync(DbContext db, int cacheSizeKb)
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
}
