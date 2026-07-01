using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly UserContext _userContext;
    private readonly AppDbContext _db;

    public AuthController(IAuthService authService, UserContext userContext, AppDbContext db)
    {
        _authService = authService;
        _userContext = userContext;
        _db = db;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Username, request.Password);

        if (result == null)
            return Unauthorized(new ErrorResult("Invalid username or password"));

        var (token, userId, username, isAdmin) = result.Value;

        // Build roots (admin has no roots)
        List<UserRootDto> roots;
        if (isAdmin)
        {
            roots = [];
        }
        else
        {
            roots = await _db.UserRoots
                .Where(r => r.UserId == userId && r.IsEnabled)
                .Select(r => new UserRootDto(r.Id, r.RootPath, r.IsEnabled, r.CreatedAt))
                .ToListAsync();
        }

        return Ok(new AuthResult(token, new UserResponse(userId, username, null, roots)));
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

        var roots = _userContext.Roots.Select(r => new UserRootDto(r.Id, r.RootPath, true, DateTime.MinValue)).ToList();

        return Ok(new UserResponse(
            _userContext.UserId,
            _userContext.Username,
            null,
            roots
        ));
    }
}
