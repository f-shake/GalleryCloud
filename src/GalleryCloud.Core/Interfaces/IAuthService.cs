namespace GalleryCloud.Core.Interfaces;

public interface IAuthService
{
    Task<(string Token, string UserId, string Username, bool IsAdmin)?> LoginAsync(string username, string password);
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}
