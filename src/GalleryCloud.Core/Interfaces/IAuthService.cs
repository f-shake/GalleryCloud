using GalleryCloud.Core.Entities;

namespace GalleryCloud.Core.Interfaces;

public interface IAuthService
{
    Task<(string Token, User User)?> LoginAsync(string username, string password);
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
    string GenerateToken(User user);
}
