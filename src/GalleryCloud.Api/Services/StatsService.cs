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
        var totalPhotos = await _db.Photos.CountAsync(p => !p.IsDeleted);
        var totalUsers = await _db.Users.CountAsync();
        var totalSize = await _db.Photos.Where(p => !p.IsDeleted).SumAsync(p => p.FileSize);
        var photosWithGps = await _db.Photos.CountAsync(p => !p.IsDeleted && p.Latitude != null);
        var formatDistribution = await _db.Photos
            .Where(p => !p.IsDeleted)
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
        var query = _db.Photos.Where(p => p.UserId == userId && !p.IsDeleted);
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
