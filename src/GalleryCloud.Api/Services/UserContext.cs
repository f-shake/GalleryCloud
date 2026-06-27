using GalleryCloud.Core.Interfaces;

namespace GalleryCloud.Api.Services;

public class UserContext : IUserContext
{
    public string UserId { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public bool IsAdmin { get; private set; }
    public string RootPath { get; private set; } = string.Empty;
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);

    public void SetUser(string userId, string username, bool isAdmin, string rootPath)
    {
        UserId = userId;
        Username = username;
        IsAdmin = isAdmin;
        RootPath = rootPath;
    }

    public void Clear()
    {
        UserId = string.Empty;
        Username = string.Empty;
        IsAdmin = false;
        RootPath = string.Empty;
    }
}
