using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GalleryCloud.Api.Data;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GalleryCloud.Api.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext db, IOptions<AuthOptions> options, ILogger<AuthService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(string Token, string UserId, string Username, bool IsAdmin)?> LoginAsync(string username, string password)
    {
        // Check admin login first (admin not in database)
        if (username == "admin")
        {
            if (password != _options.AdminDefaultPassword)
                return null;

            var token = GenerateAdminToken();
            return (token, "admin", "admin", true);
        }

        // Regular user login
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == username && !u.IsDeleted);

        if (user == null)
            return null;

        if (!VerifyPassword(password, user.PasswordHash))
            return null;

        var userToken = GenerateToken(user);
        return (userToken, user.Id, user.Username, false);
    }

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        using var hmac = new HMACSHA256(salt);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string hash)
    {
        var parts = hash.Split(':');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var storedHash = Convert.FromBase64String(parts[1]);

        using var hmac = new HMACSHA256(salt);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));

        return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
    }

    public string GenerateToken(User user)
    {
        _logger.LogInformation("Generating token for user {User}", user.Username);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: "GalleryCloud",
            audience: "GalleryCloud",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(_options.TokenExpiryDays),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateAdminToken()
    {
        _logger.LogInformation("Generating admin token");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "admin"),
            new Claim(JwtRegisteredClaimNames.UniqueName, "admin"),
            new Claim("admin", "true"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: "GalleryCloud",
            audience: "GalleryCloud",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(_options.TokenExpiryDays),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
