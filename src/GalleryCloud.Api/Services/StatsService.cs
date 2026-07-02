using GalleryCloud.Api.Data;
using GalleryCloud.Core.Dtos;
using GalleryCloud.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Services;

public class StatsService : IStatsService
{
    private readonly AppDbContext _db;

    public StatsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminStats> GetAdminStatsAsync()
    {
        var activeRootIds = await _db.UserRoots
            .Where(r => r.IsEnabled)
            .Select(r => r.Id)
            .ToListAsync();

        var query = _db.Photos.Where(p => !p.IsDeleted && activeRootIds.Contains(p.RootId));

        var totalPhotos = await query.CountAsync();
        var totalUsers = await _db.Users.CountAsync();
        var totalSize = await query.SumAsync(p => p.FileSize);
        var photosWithGps = await query.CountAsync(p => p.Latitude != null);
        var formatDistribution = await query
            .GroupBy(p => p.FileFormat)
            .Select(g => new FormatCountItem(g.Key, g.Count()))
            .ToListAsync();

        return new AdminStats(
            totalPhotos, totalUsers, totalSize,
            Math.Round(totalSize / (1024.0 * 1024 * 1024), 2),
            photosWithGps, formatDistribution
        );
    }

    public async Task<AdminStats> GetUserStatsAsync(string userId)
    {
        var activeRootIds = await _db.UserRoots
            .Where(r => r.UserId == userId && r.IsEnabled)
            .Select(r => r.Id)
            .ToListAsync();

        var query = _db.Photos.Where(p => p.UserId == userId && !p.IsDeleted && activeRootIds.Contains(p.RootId));

        var totalPhotos = await query.CountAsync();
        var totalSize = await query.SumAsync(p => p.FileSize);
        var photosWithGps = await query.CountAsync(p => p.Latitude != null);
        var formatDistribution = await query
            .GroupBy(p => p.FileFormat)
            .Select(g => new FormatCountItem(g.Key, g.Count()))
            .ToListAsync();

        return new AdminStats(
            totalPhotos, 1, totalSize,
            Math.Round(totalSize / (1024.0 * 1024 * 1024), 2),
            photosWithGps, formatDistribution
        );
    }
}
