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
    string RootId,
    int? Width,
    int? Height,
    int Orientation,
    DateTime? TakenAt,
    string? DeviceModel,
    string? ExposureTime,
    int? Iso,
    string? Aperture,
    string? FocalLength,
    int? FocalLength35mm,
    double? Latitude,
    double? Longitude,
    long FileSize,
    string? Md5Hash,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record PhotoIdentity(string Id, DateTime? TakenAt);

/// <summary>
/// 并行数组格式，消除重复 key 开销。
/// Dates[i] 为 YYYYMMDD 整数（如 20260702），null 表示无日期。
/// </summary>
public record PhotoIdsResponse(List<string> Ids, List<int?> Dates);

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
    string? DeviceModel = null,
    string? RootId = null
);

public record MessageResult(string Message);

public record ErrorResult(string Error);

public record PhotoListResponse(int Total, int Page, int Limit, List<PhotoItem> Photos);

public record SearchResponse(int Total, int Page, int Limit, List<PhotoListItem> Photos);

public record SearchFilterOptions(List<string> Formats, List<string> DeviceModels, List<TagItem> Tags);

// ============================================================
// Auth
// ============================================================

public record LoginRequest(string Username, string Password);

public record UserResponse(string Id, string Username, string? DisplayName, List<global::GalleryCloud.Core.Dtos.UserRootDto> Roots);

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

/// <summary>
/// 单日照片 IDs（平行数组格式，同 PhotoIdsResponse）。
/// </summary>
public record DateIdsResponse(string Date, List<DateIdItem> Items);

public record DateIdItem(string Id, int? DateInt);

// ============================================================
// Map
// ============================================================

public record MapPhotoPoint(string Id, string FileName, double Latitude, double Longitude, DateTime? TakenAt, int? Width, int? Height);

public record ClusterPhotoItem(string Id, string FileName, int? Width, int? Height);

public record ClusterResult(double Lat, double Lng, int Count, List<ClusterPhotoItem> Photos);

public record ClustersResponse(List<ClusterResult> Clusters, int Zoom);

public record MapPointItem(string Id, double? Latitude, double? Longitude, string FileName, DateTime? TakenAt);

public record BasemapConfig(string TileUrlNormal, string TileUrlSatellite);

// ============================================================
// Folders
// ============================================================

public class FolderNode
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? RootId { get; set; }
    public int PhotoCount { get; set; }
    public List<FolderNode> SubFolders { get; set; } = new();
}

// ============================================================
// Admin
// ============================================================

public record ThumbnailGenerationRequest(List<string>? Sizes);

public record ScanLogItem(string Id, string UserId, DateTime StartedAt, DateTime? FinishedAt, int TotalFound, int NewAdded, int SoftDeleted, string Mode);

// ============================================================
// Thumbnails
// ============================================================

public record IdsRequest(List<string> Ids, string Size, int Width);

public record ReadyResponse(List<string> Ready, List<string> Pending);

public record EnqueueResponse(int Enqueued);

// ============================================================
// Shares
// ============================================================

public record ShareItem(string Id, string Name, string Token, DateTime? ExpiresAt, DateTime CreatedAt, int PhotoCount, bool AllowDownload = true, bool AllowMetadata = true);

public record CreateShareRequest(string Name, int? ExpireDays, bool? AllowDownload = null, bool? AllowMetadata = null);

public record ExtendShareRequest(string? Name, int? ExpireDays);

public record SharePhotoItem(string Id, string FileName, string FileFormat, int? Width, int? Height);

public record ShareDetailResponse(ShareItem Share, List<SharePhotoItem> Photos);

public record AddPhotosToShareRequest(List<string> PhotoIds);

// ============================================================
// Trash
// ============================================================

public record TrashItem(
    string Id,
    string FileName,
    string FileFormat,
    int? Width,
    int? Height,
    int Orientation,
    DateTime? TakenAt,
    DateTime DeletedAt,
    long FileSize
);

public record TrashListResponse(int Total, List<TrashItem> Items);

// ============================================================
// Batch
// ============================================================

public record BatchIdsRequest(List<string> Ids);

// ============================================================
// Files
// ============================================================

public record MoveRequest(string NewRelativePath);

public record RenameRequest(string NewFileName);

// ============================================================
// User Panel
// ============================================================

public record ChangePasswordRequest(string OldPassword, string NewPassword);
