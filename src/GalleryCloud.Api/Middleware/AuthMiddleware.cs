using System.IdentityModel.Tokens.Jwt;
using System.Text;
using GalleryCloud.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using GalleryCloud.Core.Settings;

namespace GalleryCloud.Api.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, UserContext userContext, IOptions<AuthOptions> authOptions)
    {
        var token = ExtractToken(context);

        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var secret = authOptions.Value.JwtSecret;
                _logger.LogDebug("JWT Secret length: {Len}", secret.Length);
                _logger.LogDebug("Token prefix: {Prefix}", token[..Math.Min(30, token.Length)]);

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
                var handler = new JwtSecurityTokenHandler();
                // Disable claim type mapping so "sub"/"unique_name" stay as-is
                handler.InboundClaimTypeMap.Clear();

                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true,
                    ValidIssuer = "GalleryCloud",
                    ValidateAudience = true,
                    ValidAudience = "GalleryCloud",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                var principal = handler.ValidateToken(token, validationParams, out var validatedToken);
                var claims = principal.Claims;

                var userId = claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value ?? "";
                var username = claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)?.Value ?? "";
                var isAdmin = claims.FirstOrDefault(c => c.Type == "admin")?.Value == "true";
                var rootPath = claims.FirstOrDefault(c => c.Type == "rootPath")?.Value ?? "";

                userContext.SetUser(userId, username, isAdmin, rootPath);
                _logger.LogDebug("Auth OK: user={User}, admin={Admin}", username, isAdmin);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JWT validation failed: {Msg}", ex.Message);
                userContext.Clear();
            }
        }
        else
        {
            // no log for unauthenticated requests
        }

        await _next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
        // 1. Try Authorization header
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader["Bearer ".Length..].Trim();

        // 2. Fallback: query parameter (for <img> tags that can't send headers)
        if (context.Request.Query.TryGetValue("token", out var tokenFromQuery))
            return tokenFromQuery.ToString();

        return null;
    }
}
