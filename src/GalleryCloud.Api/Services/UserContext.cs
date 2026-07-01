using GalleryCloud.Api.Data;
using GalleryCloud.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Services;

public class UserContext : IUserContext
{
    private const string AdminId = "admin";

    private readonly AppDbContext _db;
    private List<UserRootInfo>? _loadedRoots;

    public string UserId { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;

    public bool IsAdmin => UserId == AdminId;

    public IReadOnlyList<UserRootInfo> Roots
    {
        get
        {
            if (_loadedRoots == null && !string.IsNullOrEmpty(UserId) && !IsAdmin)
            {
                _loadedRoots = _db.UserRoots
                    .Where(r => r.UserId == UserId && r.IsEnabled)
                    .AsNoTracking()
                    .Select(r => new UserRootInfo(r.Id, r.RootPath))
                    .ToList();
            }
            return _loadedRoots ?? [];
        }
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);

    public UserContext(AppDbContext db)
    {
        _db = db;
    }

    public void SetUser(string userId, string username)
    {
        UserId = userId;
        Username = username;
        _loadedRoots = null;
    }

    public void Clear()
    {
        UserId = string.Empty;
        Username = string.Empty;
        _loadedRoots = null;
    }
}
