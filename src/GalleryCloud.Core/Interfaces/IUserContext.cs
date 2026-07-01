namespace GalleryCloud.Core.Interfaces;

public record UserRootInfo(string Id, string RootPath);

public interface IUserContext
{
    string UserId { get; }
    string Username { get; }
    bool IsAdmin { get; }
    IReadOnlyList<UserRootInfo> Roots { get; }
    bool IsAuthenticated { get; }
}
