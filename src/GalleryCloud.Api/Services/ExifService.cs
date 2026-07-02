using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using ImageMagick;
using SixLabors.ImageSharp;

namespace GalleryCloud.Api.Services;

public static class ExifService
{
    public record ExifData(
        int Width,
        int Height,
        int Orientation,
        DateTime? TakenAt,
        string? DeviceModel,
        string? ExposureTime,
        int? Iso,
        string? Aperture,
        string? FocalLength,
        int? FocalLength35mm,
        double? Latitude,
        double? Longitude
    );

    /// <summary>
    /// 提取 EXIF 元数据。
    /// 优先使用 MetadataExtractor，失败时回退到 fallbackEngine 指定的库。
    /// </summary>
    public static ExifData Extract(string filePath, string? fallbackEngine = null)
    {
        try
        {
            return ExtractWithMetadataExtractor(filePath);
        }
        catch (Exception ex)
        {
            if ("MagickNET".Equals(fallbackEngine, StringComparison.OrdinalIgnoreCase))
                return ExtractWithMagickNet(filePath);
            if ("ImageSharp".Equals(fallbackEngine, StringComparison.OrdinalIgnoreCase))
                return ExtractWithImageSharp(filePath);
            // No fallback configured — rethrow
            throw new InvalidOperationException(
                $"MetadataExtractor failed for: {filePath}. No fallback engine configured.", ex);
        }
    }

    // ── MetadataExtractor path (primary) ─────────────────────────

    private static ExifData ExtractWithMetadataExtractor(string filePath)
    {
        // Dimensions via MagickImageInfo — reads file header only, very fast
        var info = new MagickImageInfo(filePath);
        int width = (int)info.Width;
        int height = (int)info.Height;

        var directories = ImageMetadataReader.ReadMetadata(filePath);
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();

        int orientation = 1;
        if (ifd0 != null && ifd0.TryGetInt32(ExifIfd0Directory.TagOrientation, out var orient))
            orientation = orient;

        DateTime? takenAt = null;
        if (subIfd != null && subIfd.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var dt))
            takenAt = dt;

        string? deviceModel = ifd0?.GetDescription(ExifIfd0Directory.TagModel)?.Trim();

        string? exposureTime = null;
        if (subIfd != null && subIfd.TryGetRational(ExifSubIfdDirectory.TagExposureTime, out var expRat))
            exposureTime = FormatExposure(expRat);

        int? iso = null;
        if (subIfd != null && subIfd.TryGetInt32(ExifSubIfdDirectory.TagIsoEquivalent, out var isoVal))
            iso = isoVal;

        string? aperture = null;
        if (subIfd != null && subIfd.TryGetRational(ExifSubIfdDirectory.TagFNumber, out var fNum))
            aperture = $"f/{fNum.ToDouble():F1}";

        string? focalLength = null;
        if (subIfd != null && subIfd.TryGetRational(ExifSubIfdDirectory.TagFocalLength, out var fl))
            focalLength = $"{fl.ToDouble():F0}mm";

        int? focalLength35mm = null;
        if (subIfd != null && subIfd.TryGetInt32(42005, out var fl35)) // FocalLengthIn35mmFilm
            focalLength35mm = fl35;

        double? latitude = null;
        double? longitude = null;
        if (gpsDir != null && gpsDir.TryGetGeoLocation(out var geo))
        {
            latitude = double.IsNaN(geo.Latitude) || double.IsInfinity(geo.Latitude) ? null : geo.Latitude;
            longitude = double.IsNaN(geo.Longitude) || double.IsInfinity(geo.Longitude) ? null : geo.Longitude;
        }

        return new ExifData(width, height, orientation, takenAt, deviceModel,
            exposureTime, iso, aperture, focalLength, focalLength35mm, latitude, longitude);
    }

    // ── Magick.NET fallback ──────────────────────────────────────

    private static ExifData ExtractWithMagickNet(string filePath)
    {
        using var image = new MagickImage(filePath);
        int width = (int)image.Width;
        int height = (int)image.Height;

        int orientation = 1;
        DateTime? takenAt = null;
        string? deviceModel = null;
        string? exposureTime = null;
        int? iso = null;
        string? aperture = null;
        string? focalLength = null;
        int? focalLength35mm = null;
        double? latitude = null;
        double? longitude = null;

        var exif = image.GetExifProfile();
        if (exif != null)
        {
            var orientVal = exif.GetValue(ImageMagick.ExifTag.Orientation);
            if (orientVal != null) orientation = orientVal.Value;

            var dateVal = exif.GetValue(ImageMagick.ExifTag.DateTimeOriginal);
            if (dateVal != null) takenAt = ParseExifDate(dateVal.Value);

            var modelVal = exif.GetValue(ImageMagick.ExifTag.Model);
            if (modelVal != null) deviceModel = modelVal.Value.Trim();

            var expVal = exif.GetValue(ImageMagick.ExifTag.ExposureTime);
            if (expVal != null) exposureTime = FormatExposureMagick(expVal.Value);

            var isoVal = exif.GetValue(ImageMagick.ExifTag.ISOSpeedRatings);
            if (isoVal != null) iso = isoVal.Value[0];

            var fNumVal = exif.GetValue(ImageMagick.ExifTag.FNumber);
            if (fNumVal != null) aperture = $"f/{fNumVal.Value.ToDouble():F1}";

            var flVal = exif.GetValue(ImageMagick.ExifTag.FocalLength);
            if (flVal != null) focalLength = $"{flVal.Value.ToDouble():F0}mm";

            var fl35Val = exif.GetValue(ImageMagick.ExifTag.FocalLengthIn35mmFilm);
            if (fl35Val != null) focalLength35mm = (int)fl35Val.Value;

            // GPS
            var gpsLat = exif.GetValue(ImageMagick.ExifTag.GPSLatitude);
            var gpsLatRef = exif.GetValue(ImageMagick.ExifTag.GPSLatitudeRef);
            var gpsLon = exif.GetValue(ImageMagick.ExifTag.GPSLongitude);
            var gpsLonRef = exif.GetValue(ImageMagick.ExifTag.GPSLongitudeRef);

            if (gpsLat != null && gpsLon != null)
            {
                latitude = ParseGpsCoordinate(gpsLat.Value, gpsLatRef?.Value);
                longitude = ParseGpsCoordinate(gpsLon.Value, gpsLonRef?.Value);
            }
        }

        return new ExifData(width, height, orientation, takenAt, deviceModel,
            exposureTime, iso, aperture, focalLength, focalLength35mm, latitude, longitude);
    }

    // ── ImageSharp fallback (original Identify-based approach) ───

    private static ExifData ExtractWithImageSharp(string filePath)
    {
        var imageInfo = Image.Identify(filePath);
        int width = imageInfo.Width;
        int height = imageInfo.Height;

        int orientation = 1;
        DateTime? takenAt = null;
        string? deviceModel = null;
        string? exposureTime = null;
        int? iso = null;
        string? aperture = null;
        string? focalLength = null;
        int? focalLength35mm = null;
        double? latitude = null;
        double? longitude = null;

        var exif = imageInfo.Metadata?.ExifProfile;
        if (exif != null)
        {
            SixLabors.ImageSharp.Rational[]? gpsLat = null, gpsLon = null;
            string? gpsLatRef = null, gpsLonRef = null;

            foreach (var entry in exif.Values)
            {
                var tag = entry.Tag;

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<ushort> orientEntry)
                    orientation = orientEntry.Value;

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.DateTimeOriginal
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<string> dateEntry
                    && !string.IsNullOrWhiteSpace(dateEntry.Value))
                    takenAt = ParseExifDate(dateEntry.Value);

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Model
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<string> modelEntry)
                    deviceModel = modelEntry.Value?.Trim();

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.ExposureTime
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<SixLabors.ImageSharp.Rational> expEntry)
                    exposureTime = FormatExposureImageSharp(expEntry.Value);

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.ISOSpeedRatings
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<ushort> isoEntry)
                    iso = isoEntry.Value;

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.FNumber
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<SixLabors.ImageSharp.Rational> fNumEntry)
                    aperture = $"f/{fNumEntry.Value.ToDouble():F1}";

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.FocalLength
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<SixLabors.ImageSharp.Rational> flEntry)
                    focalLength = $"{flEntry.Value.ToDouble():F0}mm";

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.FocalLengthIn35mmFilm
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<ushort> fl35Entry)
                    focalLength35mm = fl35Entry.Value;

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.GPSLatitude
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<SixLabors.ImageSharp.Rational[]> gLatEntry)
                    gpsLat = gLatEntry.Value;

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.GPSLatitudeRef
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<string> gLatRefEntry)
                    gpsLatRef = gLatRefEntry.Value;

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.GPSLongitude
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<SixLabors.ImageSharp.Rational[]> gLonEntry)
                    gpsLon = gLonEntry.Value;

                if (tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.GPSLongitudeRef
                    && entry is SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue<string> gLonRefEntry)
                    gpsLonRef = gLonRefEntry.Value;
            }

            if (gpsLat != null && gpsLon != null)
            {
                latitude = ParseGpsCoordinate(gpsLat, gpsLatRef);
                longitude = ParseGpsCoordinate(gpsLon, gpsLonRef);
            }
        }

        return new ExifData(width, height, orientation, takenAt, deviceModel,
            exposureTime, iso, aperture, focalLength, focalLength35mm, latitude, longitude);
    }

    // ── FormatExposure overloads ─────────────────────────────────

    private static string FormatExposure(MetadataExtractor.Rational rational)
    {
        if (rational.Denominator == 0) return rational.ToString();
        if (rational.Denominator > 1 && rational.Numerator == 1)
            return $"{rational.Numerator}/{rational.Denominator}";
        var val = rational.ToDouble();
        if (val < 1)
            return $"1/{Math.Round(1 / val)}";
        return val.ToString("F1");
    }

    private static string FormatExposureMagick(ImageMagick.Rational rational)
    {
        if (rational.Denominator == 0) return rational.ToString();
        if (rational.Denominator > 1 && rational.Numerator == 1)
            return $"{rational.Numerator}/{rational.Denominator}";
        var val = rational.ToDouble();
        if (val < 1)
            return $"1/{Math.Round(1 / val)}";
        return val.ToString("F1");
    }

    private static string FormatExposureImageSharp(SixLabors.ImageSharp.Rational rational)
    {
        if (rational.Denominator == 0) return rational.ToString();
        if (rational.Denominator > 1 && rational.Numerator == 1)
            return $"{rational.Numerator}/{rational.Denominator}";
        var val = rational.ToDouble();
        if (val < 1)
            return $"1/{Math.Round(1 / val)}";
        return val.ToString("F1");
    }

    // ── GPS coordinate parsing ───────────────────────────────────

    private static double? ParseGpsCoordinate(ImageMagick.Rational[] parts, string? reference)
    {
        if (parts.Length < 3) return null;

        var result = parts[0].ToDouble() + parts[1].ToDouble() / 60.0 + parts[2].ToDouble() / 3600.0;

        if (double.IsNaN(result) || double.IsInfinity(result))
            return null;

        if (reference == "S" || reference == "W")
            result = -result;

        return result;
    }

    private static double? ParseGpsCoordinate(SixLabors.ImageSharp.Rational[] parts, string? reference)
    {
        if (parts.Length < 3) return null;

        var result = parts[0].ToDouble() + parts[1].ToDouble() / 60.0 + parts[2].ToDouble() / 3600.0;

        if (double.IsNaN(result) || double.IsInfinity(result))
            return null;

        if (reference == "S" || reference == "W")
            result = -result;

        return result;
    }

    // ── Date parsing ─────────────────────────────────────────────

    private static DateTime? ParseExifDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        if (dateStr.Length >= 10)
            dateStr = dateStr[..4] + "-" + dateStr[5..7] + "-" + dateStr[8..];

        return DateTime.TryParse(dateStr, out var dt) ? dt : null;
    }
}
