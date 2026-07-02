using GalleryCloud.Core.Dtos;

namespace GalleryCloud.Core.Interfaces;

public interface IStatsService
{
    Task<AdminStats> GetAdminStatsAsync();
    Task<AdminStats> GetUserStatsAsync(string userId);
}
