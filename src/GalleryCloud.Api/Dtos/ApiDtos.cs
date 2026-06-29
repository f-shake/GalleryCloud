// ReSharper disable NotAccessedPositionalProperty.Global
namespace GalleryCloud.Api.Dtos;

// ============================================================
// Common — used across multiple controllers
// ============================================================

public record PhotoItem(
    string Id,
    string FileName,
    string FileFormat,
    int? Width,
    int? Height,
    int Orientation,
    DateTime? TakenAt,
    double? Latitude,
    double? Longitude,
    long FileSize,
    string? DeviceModel = null,
    DateTime? CreatedAt = null
);

public record PhotoDetail(
    string Id,
    string FileName,
    string FileFormat,
    string FilePath,
    int? Width,
    int? Height,
    int Orientation,
    DateTime? TakenAt,
    string? DeviceModel,
    double? Latitude,
    double? Longitude,
    long FileSize,
    string? Md5Hash,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record PhotoIdentity(string Id, DateTime? TakenAt);

public record PhotoListItem(
    string Id,
    string FileName,
    string FileFormat,
    int? Width,
    int? Height,
    int Orientation,
    DateTime? TakenAt,
    double? Latitude,
    double? Longitude,
    long FileSize,
    string? FilePath,
    bool? IsDeleted,
    string? DeviceModel = null
);

public record MessageResult(string Message);

public record ErrorResult(string Error);

public record PhotoListResponse(int Total, int Page, int Limit, List<PhotoItem> Photos);

public record SearchResponse(int Total, int Page, int Limit, List<PhotoListItem> Photos);

// ============================================================
// Auth
// ============================================================

public record LoginRequest(string Username, string Password);

public record UserResponse(string Id, string Username, string? DisplayName, bool IsAdmin, string RootPath);

public record AuthResult(string Token, UserResponse User);

// ============================================================
// Tags
// ============================================================

public record TagItem(string Id, string Name, string? Color);

public record CreateTagRequest(string Name, string? Color);

// ============================================================
// Favorites
// ============================================================

public record FavoriteResult(bool Favorited);

// ============================================================
// Timeline
// ============================================================

public record TimelineGroup(string? Label, string? Cursor, List<PhotoItem> Photos);

public record TimelineResponse(List<TimelineGroup> Groups, string? NextCursor, bool HasMore);

public record DailyDensityItem(string Date, int Count);

public record YearCountItem(int Year, int Count);

public record MonthlyDensityItem(int Year, int[] MonthCounts);

// ============================================================
// Map
// ============================================================

public record MapPhotoPoint(string Id, string FileName, double Latitude, double Longitude, DateTime? TakenAt, int? Width, int? Height);

public record ClusterPhotoItem(string Id, string FileName, int? Width, int? Height);

public record ClusterResult(double Lat, double Lng, int Count, List<ClusterPhotoItem> Photos);

public record ClustersResponse(List<ClusterResult> Clusters, int Zoom);

public record MapPointItem(string Id, double? Latitude, double? Longitude, string FileName, DateTime? TakenAt);

public record BasemapConfig(string TileUrlNormal, string TileUrlSatellite, string DefaultBasemap);

// ============================================================
// Folders
// ============================================================

public class FolderNode
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public int PhotoCount { get; set; }
    public List<FolderNode> SubFolders { get; set; } = new();
}

// ============================================================
// Admin
// ============================================================

public record CreateUserRequest(string Username, string Password, string? DisplayName, string RootPath, bool IsAdmin);

public record UpdateUserRequest(string? Password, string? DisplayName, string? RootPath, bool? IsAdmin, bool? IsActive);

public record UserListItem(string Id, string Username, string? DisplayName, string RootPath, bool IsAdmin, bool IsActive, DateTime CreatedAt);

public record ThumbnailGenerationRequest(List<string>? Sizes);

public record FormatCountItem(string Format, int Count);

public record AdminStats(
    long TotalPhotos,
    int TotalUsers,
    long TotalSize,
    double TotalSizeGb,
    int PhotosWithGps,
    List<FormatCountItem> FormatDistribution
);

public record ScanLogItem(string Id, string UserId, DateTime StartedAt, DateTime? FinishedAt, int TotalFound, int NewAdded, int SoftDeleted, string Mode);

// ============================================================
// Thumbnails
// ============================================================

public record IdsRequest(List<string> Ids, string Size, int Width);

public record ReadyResponse(List<string> Ready, List<string> Pending);

public record EnqueueResponse(int Enqueued);

// ============================================================
// Files
// ============================================================

public record MoveRequest(string NewRelativePath);

public record RenameRequest(string NewFileName);
