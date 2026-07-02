namespace GalleryCloud.Core.Dtos;

public record UserRootDto(string Id, string RootPath, bool IsEnabled, DateTime CreatedAt);
public record CreateUserRootRequest(string RootPath);
public record CreateUserRequest(string Username, string Password, string? DisplayName, List<string> RootPaths);
public record UpdateUserRequest(string? Password, string? DisplayName, bool? IsActive, List<string>? RootPaths = null);
public record UserListItem(string Id, string Username, string? DisplayName, bool IsActive, DateTime CreatedAt, List<UserRootDto> Roots);
