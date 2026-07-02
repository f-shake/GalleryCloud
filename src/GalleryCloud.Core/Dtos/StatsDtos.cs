namespace GalleryCloud.Core.Dtos;

public record FormatCountItem(string Format, int Count);

public record AdminStats(
    long TotalPhotos,
    int TotalUsers,
    long TotalSize,
    double TotalSizeGb,
    int PhotosWithGps,
    List<FormatCountItem> FormatDistribution
);
