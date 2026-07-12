using System.ComponentModel.DataAnnotations;

namespace GalleryCloud.Core.Settings;

public class AuthOptions
{
    public const string DefaultJwtSecret = "change-me-to-a-random-string-at-least-32-chars!!";
    public const string DefaultAdminPassword = "admin";

    [Required(ErrorMessage = "Auth:JwtSecret is required. Set it in appsettings.json or via environment variable.")]
    public string JwtSecret { get; set; } = string.Empty;

    public int TokenExpiryDays { get; set; } = 30;

    [Required(ErrorMessage = "Auth:AdminDefaultPassword is required. Set it in appsettings.json or via environment variable.")]
    public string AdminDefaultPassword { get; set; } = string.Empty;
}
