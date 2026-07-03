using GalleryCloud.Api.Services;
using GalleryCloud.Core.Dtos;
using GalleryCloud.Core.Settings;
using Microsoft.Extensions.Options;

namespace GalleryCloud.Tests;

public class UserServiceTest : ServiceTestBase
{
    private UserService CreateService()
    {
        var authOptions = Options.Create(new AuthOptions { JwtSecret = "test-secret-key-12345" });
        var auth = new AuthService(_db, authOptions, null!);
        return new UserService(_db, auth, new StubScanService(), new StubThumbnailService(), null!, null!);
    }

    [Fact]
    public async Task ListUsers_ReturnsAllUsers()
    {
        CreateUser("alice");
        CreateUser("bob");
        var svc = CreateService();
        var users = await svc.ListUsersAsync();
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public async Task ListUsers_ExcludesDeletedRoots()
    {
        var user = CreateUser("alice");
        CreateRoot(user, @"C:\Photos", isDeleted: true);
        CreateRoot(user, @"C:\Other", isEnabled: false);

        var svc = CreateService();
        var users = await svc.ListUsersAsync();
        var alice = users.First(u => u.Username == "alice");
        Assert.Empty(alice.Roots);
    }

    [Fact]
    public async Task CreateUser_ValidRequest_CreatesUser()
    {
        var request = new CreateUserRequest("newuser", "password123", "New User",
            new List<string> { @"C:\Photos" });

        var svc = CreateService();
        var result = await svc.CreateUserAsync(request);

        Assert.Equal("newuser", result.Username);
        Assert.Equal("New User", result.DisplayName);
        Assert.True(result.IsActive);
        Assert.Single(result.Roots);
        Assert.Equal(@"C:\Photos", result.Roots[0].RootPath);
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_Throws()
    {
        CreateUser("existing");
        var request = new CreateUserRequest("existing", "pass", null,
            new List<string> { @"C:\Photos" });
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateUserAsync(request));
    }

    [Fact]
    public async Task CreateUser_EmptyPassword_Throws()
    {
        var request = new CreateUserRequest("u", "", null, new List<string> { @"C:\Photos" });
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateUserAsync(request));
    }

    [Fact]
    public async Task CreateUser_NoRoots_Throws()
    {
        var request = new CreateUserRequest("u", "pass", null, new List<string>());
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateUserAsync(request));
    }

    [Fact]
    public async Task CreateUser_NestedRoots_Throws()
    {
        var request = new CreateUserRequest("u", "pass", null,
            new List<string> { @"C:\Photos", @"C:\Photos\Vacation" });
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateUserAsync(request));
    }

    [Fact]
    public async Task UpdateUser_ChangePassword_AndDisplayName()
    {
        var user = CreateUser("u1", "Old Name");
        var request = new UpdateUserRequest("newpass", "New Name", null);

        var svc = CreateService();
        var result = await svc.UpdateUserAsync(user.Id, request);
        Assert.NotNull(result);
        Assert.Equal("New Name", result.DisplayName);

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.NotNull(updated);
        Assert.Equal("New Name", updated.DisplayName);
    }

    [Fact]
    public async Task UpdateUser_DisableUser()
    {
        var user = CreateUser("u1");
        var request = new UpdateUserRequest(null, null, false);

        var svc = CreateService();
        await svc.UpdateUserAsync(user.Id, request);

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsDeleted);
    }

    [Fact]
    public async Task UpdateUser_NotFound_ReturnsNull()
    {
        var request = new UpdateUserRequest(null, null, true);
        var svc = CreateService();
        var result = await svc.UpdateUserAsync("nonexistent", request);
        Assert.Null(result);
    }

    [Fact]
    public async Task DisableUser_SetsDeletedFlag()
    {
        var user = CreateUser("u1");
        var svc = CreateService();
        var success = await svc.DisableUserAsync(user.Id);
        Assert.True(success);

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.True(updated!.IsDeleted);
    }

    [Fact]
    public async Task DisableUser_NotFound_ReturnsFalse()
    {
        var svc = CreateService();
        var result = await svc.DisableUserAsync("nonexistent");
        Assert.False(result);
    }

    [Fact]
    public async Task ListRoots_ReturnsRootsForUser()
    {
        var user = CreateUser("u1");
        CreateRoot(user, @"C:\Photos");
        CreateRoot(user, @"D:\Backup");

        var svc = CreateService();
        var roots = await svc.ListRootsAsync(user.Id);
        Assert.Equal(2, roots.Count);
    }

    [Fact]
    public async Task AddRoot_ValidPath_AddsRoot()
    {
        var user = CreateUser("u1");
        CreateRoot(user, @"C:\Photos");

        var svc = CreateService();
        var result = await svc.AddRootAsync(user.Id, @"D:\Other");
        Assert.NotNull(result);
        Assert.Equal(@"D:\Other", result.RootPath);
    }

    [Fact]
    public async Task AddRoot_NestedPath_Throws()
    {
        var user = CreateUser("u1");
        CreateRoot(user, @"C:\Photos");

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AddRootAsync(user.Id, @"C:\Photos\Vacation"));
    }

    [Fact]
    public async Task AddRoot_UserNotFound_ReturnsNull()
    {
        var svc = CreateService();
        var result = await svc.AddRootAsync("nonexistent", "C:\\Test");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteRoot_SoftDeletes()
    {
        var user = CreateUser("u1");
        var root = CreateRoot(user, @"C:\Photos");

        var svc = CreateService();
        var success = await svc.DeleteRootAsync(user.Id, root.Id);
        Assert.True(success);

        var updated = await _db.UserRoots.FindAsync(root.Id);
        Assert.True(updated!.IsDeleted);
    }

    [Fact]
    public async Task DeleteRoot_NotFound_ReturnsFalse()
    {
        var svc = CreateService();
        var result = await svc.DeleteRootAsync("u", "nonexistent");
        Assert.False(result);
    }

    [Fact]
    public void HasNesting_DetectsNestedPaths()
    {
        var svc = CreateService();
        var paths = new List<string> { @"C:\Photos", @"C:\Photos\Vacation" };
        Assert.True(svc.HasNesting(paths));
    }

    [Fact]
    public void HasNesting_NoNesting()
    {
        var svc = CreateService();
        var paths = new List<string> { @"C:\Photos", @"D:\Backup" };
        Assert.False(svc.HasNesting(paths));
    }
}
