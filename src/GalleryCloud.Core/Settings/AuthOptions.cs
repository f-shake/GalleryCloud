namespace GalleryCloud.Core.Settings;

public class AuthOptions
{
    public string JwtSecret { get; set; } = "change-me-to-a-random-string-at-least-32-chars";
    public int TokenExpiryDays { get; set; } = 30;
    public string AdminDefaultPassword { get; set; } = "admin";
}
