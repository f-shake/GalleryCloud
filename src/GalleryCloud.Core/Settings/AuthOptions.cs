using System.ComponentModel.DataAnnotations;

namespace GalleryCloud.Core.Settings;

public class AuthOptions : IValidatableObject
{
    internal const string DefaultJwtSecret = "change-me-to-a-random-string-at-least-32-chars!!";
    internal const string DefaultAdminPassword = "admin";

    [Required(ErrorMessage = "Auth:JwtSecret is required. Set it in appsettings.json or via environment variable.")]
    public string JwtSecret { get; set; } = string.Empty;

    public int TokenExpiryDays { get; set; } = 30;

    [Required(ErrorMessage = "Auth:AdminDefaultPassword is required. Set it in appsettings.json or via environment variable.")]
    public string AdminDefaultPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (JwtSecret == DefaultJwtSecret)
            yield return new ValidationResult(
                "Auth:JwtSecret must not be the default placeholder. Generate a new random key and set it in appsettings.json or environment variable.",
                [nameof(JwtSecret)]);

        if (AdminDefaultPassword == DefaultAdminPassword)
            yield return new ValidationResult(
                "Auth:AdminDefaultPassword must not be the default value. Change it in appsettings.json or environment variable.",
                [nameof(AdminDefaultPassword)]);
    }
}
