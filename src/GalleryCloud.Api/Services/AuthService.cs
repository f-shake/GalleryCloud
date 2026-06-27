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

    public async Task<(string Token, User User)?> LoginAsync(string username, string password)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null)
            return null;

        if (!VerifyPassword(password, user.PasswordHash))
            return null;

        var token = GenerateToken(user);
        return (token, user);
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
        _logger.LogInformation("Generating token with secret len={Len}, expiry={Days}d",
            _options.JwtSecret.Length, _options.TokenExpiryDays);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim("admin", user.IsAdmin ? "true" : "false"),
            new Claim("rootPath", user.RootPath),
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
