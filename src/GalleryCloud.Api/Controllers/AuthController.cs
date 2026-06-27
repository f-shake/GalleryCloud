using GalleryCloud.Api.Services;
using GalleryCloud.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly UserContext _userContext;

    public AuthController(IAuthService authService, UserContext userContext)
    {
        _authService = authService;
        _userContext = userContext;
    }

    public record LoginRequest(string Username, string Password);
    public record UserResponse(string Id, string Username, string? DisplayName, bool IsAdmin, string RootPath);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Username, request.Password);

        if (result == null)
            return Unauthorized(new { error = "Invalid username or password" });

        var (token, user) = result.Value;

        return Ok(new
        {
            token,
            user = new UserResponse(user.Id, user.Username, user.DisplayName, user.IsAdmin, user.RootPath)
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized(new { error = "Not authenticated" });

        return Ok(new UserResponse(
            _userContext.UserId,
            _userContext.Username,
            null,
            _userContext.IsAdmin,
            _userContext.RootPath
        ));
    }
}
