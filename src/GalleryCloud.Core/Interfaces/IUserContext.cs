namespace GalleryCloud.Core.Interfaces;

public interface IUserContext
{
    string UserId { get; }
    string Username { get; }
    bool IsAdmin { get; }
    string RootPath { get; }
}
