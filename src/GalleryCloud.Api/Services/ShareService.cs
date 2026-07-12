using System.Security.Cryptography;
using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Services;

public class ShareService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ShareService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<ShareDetailResponse> CreateShareAsync(string userId, string name, int? expireDays,
        bool? allowDownload = null, bool? allowMetadata = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var share = new Share
        {
            UserId = userId,
            Name = name,
            Token = GenerateToken(),
            ExpiresAt = expireDays.HasValue ? DateTime.UtcNow.AddDays(expireDays.Value) : null,
            AllowDownload = allowDownload ?? true,
            AllowMetadata = allowMetadata ?? true,
        };

        db.Shares.Add(share);
        await db.SaveChangesAsync();

        return MapToDetail(share, []);
    }

    public async Task<ShareDetailResponse> GetShareAsync(string shareId, string userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var share = await db.Shares
            .Include(s => s.SharePhotos.Where(sp => !sp.IsDeleted))
            .ThenInclude(sp => sp.Photo)
            .FirstOrDefaultAsync(s => s.Id == shareId && s.UserId == userId && !s.IsDeleted);

        if (share == null) throw new KeyNotFoundException("Share not found");

        return MapToDetail(share, share.SharePhotos.Where(sp => sp.Photo != null).ToList());
    }

    public async Task<ShareDetailResponse> GetPublicShareAsync(string token)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var share = await db.Shares
            .Include(s => s.SharePhotos.Where(sp => !sp.IsDeleted))
            .ThenInclude(sp => sp.Photo)
            .FirstOrDefaultAsync(s => s.Token == token && !s.IsDeleted);

        if (share == null) throw new KeyNotFoundException("Share not found or expired");
        if (share.ExpiresAt.HasValue && share.ExpiresAt.Value < DateTime.UtcNow)
            throw new InvalidOperationException("Share has expired");

        return MapToDetail(share, share.SharePhotos.Where(sp => sp.Photo != null).ToList());
    }

    public async Task<ShareDetailResponse> AddPhotosToShareAsync(string shareId, string userId, List<string> photoIds)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var share = await db.Shares
            .Include(s => s.SharePhotos.Where(sp => !sp.IsDeleted))
            .FirstOrDefaultAsync(s => s.Id == shareId && s.UserId == userId && !s.IsDeleted);
        if (share == null) throw new KeyNotFoundException("Share not found");

        // Verify all photos belong to the user
        var ownedCount = await db.Photos
            .CountAsync(p => photoIds.Contains(p.Id) && p.UserId == userId);
        if (ownedCount != photoIds.Count)
            throw new InvalidOperationException("Some photos do not belong to you");

        var existingIds = share.SharePhotos.Select(sp => sp.PhotoId).ToHashSet();
        foreach (var pid in photoIds)
        {
            if (existingIds.Contains(pid)) continue;
            db.SharePhotos.Add(new SharePhoto { ShareId = shareId, PhotoId = pid });
        }

        await db.SaveChangesAsync();

        // Reload with photos
        return await GetShareAsync(shareId, userId);
    }

    public async Task RemovePhotoFromShareAsync(string shareId, string userId, string photoId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var share = await db.Shares
            .FirstOrDefaultAsync(s => s.Id == shareId && s.UserId == userId && !s.IsDeleted);
        if (share == null) throw new KeyNotFoundException("Share not found");

        var sp = await db.SharePhotos
            .FirstOrDefaultAsync(sp => sp.ShareId == shareId && sp.PhotoId == photoId && !sp.IsDeleted);
        if (sp != null)
        {
            sp.IsDeleted = true;
            sp.DeletedAt = DateTime.UtcNow;
            sp.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    public async Task<List<ShareItem>> ListSharesAsync(string userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var shares = await db.Shares
            .Where(s => s.UserId == userId && !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ShareItem(
                s.Id, s.Name, s.Token, s.ExpiresAt, s.CreatedAt,
                s.SharePhotos.Count(sp => !sp.IsDeleted),
                s.AllowDownload, s.AllowMetadata
            ))
            .ToListAsync();

        return shares;
    }

    public async Task ExtendShareAsync(string shareId, string userId, string? name, int? expireDays)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var share = await db.Shares
            .FirstOrDefaultAsync(s => s.Id == shareId && s.UserId == userId && !s.IsDeleted);
        if (share == null) throw new KeyNotFoundException("Share not found");

        if (name != null) share.Name = name;
        if (expireDays.HasValue)
            share.ExpiresAt = expireDays.Value == 0 ? null : DateTime.UtcNow.AddDays(expireDays.Value);
        share.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task DeleteShareAsync(string shareId, string userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var share = await db.Shares
            .FirstOrDefaultAsync(s => s.Id == shareId && s.UserId == userId && !s.IsDeleted);
        if (share == null) throw new KeyNotFoundException("Share not found");

        share.IsDeleted = true;
        share.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static string GenerateToken()
    {
        var bytes = new byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("/", "_")
            .Replace("+", "-")
            .Replace("=", "");
    }

    private static ShareDetailResponse MapToDetail(Share share, List<SharePhoto> sharePhotos)
    {
        var photos = sharePhotos
            .Where(sp => sp.Photo != null)
            .Select(sp => new SharePhotoItem(
                sp.Photo!.Id,
                sp.Photo.FileName,
                sp.Photo.FileFormat,
                sp.Photo.Width,
                sp.Photo.Height
            ))
            .ToList();

        var item = new ShareItem(
            share.Id, share.Name, share.Token,
            share.ExpiresAt, share.CreatedAt, photos.Count,
            share.AllowDownload, share.AllowMetadata
        );

        return new ShareDetailResponse(item, photos);
    }
}
