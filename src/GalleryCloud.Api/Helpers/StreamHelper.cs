namespace GalleryCloud.Api.Helpers;

public static class StreamHelper
{
    public static async Task<byte[]> ReadFullyAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
