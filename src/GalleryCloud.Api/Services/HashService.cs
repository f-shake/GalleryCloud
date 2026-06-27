using System.Security.Cryptography;

namespace GalleryCloud.Api.Services;

public class HashService
{
    public static async Task<string> ComputeMd5Async(string filePath)
    {
        using var md5 = MD5.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await md5.ComputeHashAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeMd5(Stream stream)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }
}
