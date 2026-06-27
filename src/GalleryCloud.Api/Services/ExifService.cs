using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace GalleryCloud.Api.Services;

public class ExifService
{
    public record ExifData(
        int Width,
        int Height,
        int Orientation,
        DateTime? TakenAt,
        string? DeviceModel,
        double? Latitude,
        double? Longitude
    );

    public static ExifData Extract(string filePath)
    {
        var imageInfo = Image.Identify(filePath);
        return BuildExifData(imageInfo);
    }

    public static ExifData Extract(Stream stream)
    {
        var imageInfo = Image.Identify(stream);
        return BuildExifData(imageInfo);
    }

    private static ExifData BuildExifData(ImageInfo imageInfo)
    {
        int orientation = 1;
        DateTime? takenAt = null;
        string? deviceModel = null;
        double? latitude = null;
        double? longitude = null;

        var exif = imageInfo.Metadata.ExifProfile;
        if (exif != null)
        {
            Rational[]? gpsLat = null, gpsLon = null;
            string? gpsLatRef = null, gpsLonRef = null;

            foreach (var entry in exif.Values)
            {
                var tag = entry.Tag;

                if (tag == ExifTag.Orientation && entry is IExifValue<ushort> orientEntry)
                    orientation = orientEntry.Value;

                if (tag == ExifTag.DateTimeOriginal && entry is IExifValue<string> dateEntry && !string.IsNullOrWhiteSpace(dateEntry.Value))
                    takenAt = ParseExifDate(dateEntry.Value);

                if (tag == ExifTag.Model && entry is IExifValue<string> modelEntry)
                    deviceModel = modelEntry.Value?.Trim();

                if (tag == ExifTag.GPSLatitude && entry is IExifValue<Rational[]> gLatEntry)
                    gpsLat = gLatEntry.Value;

                if (tag == ExifTag.GPSLatitudeRef && entry is IExifValue<string> gLatRefEntry)
                    gpsLatRef = gLatRefEntry.Value;

                if (tag == ExifTag.GPSLongitude && entry is IExifValue<Rational[]> gLonEntry)
                    gpsLon = gLonEntry.Value;

                if (tag == ExifTag.GPSLongitudeRef && entry is IExifValue<string> gLonRefEntry)
                    gpsLonRef = gLonRefEntry.Value;
            }

            if (gpsLat != null && gpsLon != null)
            {
                latitude = ParseGpsCoordinate(gpsLat, gpsLatRef);
                longitude = ParseGpsCoordinate(gpsLon, gpsLonRef);
            }
        }

        return new ExifData(
            imageInfo.Width, imageInfo.Height, orientation,
            takenAt, deviceModel, latitude, longitude);
    }

    private static DateTime? ParseExifDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        // Format: "2024:06:15 14:30:00" or "2024-06-15 14:30:00"
        if (dateStr.Length >= 10)
            dateStr = dateStr[..4] + "-" + dateStr[5..7] + "-" + dateStr[8..];

        return DateTime.TryParse(dateStr, out var dt) ? dt : null;
    }

    private static double? ParseGpsCoordinate(Rational[] parts, string? reference)
    {
        if (parts.Length < 3) return null;

        var result = parts[0].ToDouble() + parts[1].ToDouble() / 60.0 + parts[2].ToDouble() / 3600.0;

        if (reference == "S" || reference == "W")
            result = -result;

        return result;
    }
}
