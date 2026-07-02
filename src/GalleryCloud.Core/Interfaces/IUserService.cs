using GalleryCloud.Core.Dtos;

namespace GalleryCloud.Core.Interfaces;

/// <summary>
/// 用户和根目录管理服务。
/// 方法返回 null 表示未找到，抛出 InvalidOperationException 表示业务规则冲突。
/// </summary>
public interface IUserService
{
    Task<List<UserListItem>> ListUsersAsync();

    /// <summary>
    /// 创建用户。验证失败时抛出 InvalidOperationException。
    /// </summary>
    Task<UserListItem> CreateUserAsync(CreateUserRequest request);

    /// <summary>
    /// 更新用户。用户不存在返回 null，验证失败抛出 InvalidOperationException。
    /// </summary>
    Task<UserListItem?> UpdateUserAsync(string id, UpdateUserRequest request);

    /// <summary>
    /// 软删除用户。用户不存在返回 null。
    /// </summary>
    Task<bool> DisableUserAsync(string id);

    Task<List<UserRootDto>> ListRootsAsync(string userId);

    /// <summary>
    /// 添加根目录。用户不存在返回 null，验证失败抛出 InvalidOperationException。
    /// </summary>
    Task<UserRootDto?> AddRootAsync(string userId, string rootPath);

    /// <summary>
    /// 删除根目录。根目录不存在返回 null。
    /// </summary>
    Task<bool> DeleteRootAsync(string userId, string rootId);

    /// <summary>
    /// 检查路径列表中是否存在相互嵌套。
    /// </summary>
    bool HasNesting(List<string> roots);
}
