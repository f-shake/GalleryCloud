using GalleryCloud.Api.Dtos;
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

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Username, request.Password);

        if (result == null)
            return Unauthorized(new ErrorResult("Invalid username or password"));

        var (token, user) = result.Value;

        return Ok(new AuthResult(token, new UserResponse(user.Id, user.Username, user.DisplayName, user.IsAdmin, user.RootPath)));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return Ok(new MessageResult("Logged out"));
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!_userContext.IsAuthenticated)
            return Unauthorized(new ErrorResult("Not authenticated"));

        return Ok(new UserResponse(
            _userContext.UserId,
            _userContext.Username,
            null,
            _userContext.IsAdmin,
            _userContext.RootPath
        ));
    }
}
