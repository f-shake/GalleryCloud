using System.Text.Json.Serialization;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GalleryCloud.Api.Dtos;

[JsonSerializable(typeof(PhotoItem))]
[JsonSerializable(typeof(PhotoDetail))]
[JsonSerializable(typeof(PhotoIdentity))]
[JsonSerializable(typeof(PhotoIdsResponse))]
[JsonSerializable(typeof(PhotoListItem))]
[JsonSerializable(typeof(MessageResult))]
[JsonSerializable(typeof(ErrorResult))]
[JsonSerializable(typeof(PhotoListResponse))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(SearchFilterOptions))]

[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(AuthResult))]
[JsonSerializable(typeof(UserRootDto))]
[JsonSerializable(typeof(CreateUserRootRequest))]
[JsonSerializable(typeof(List<UserRootDto>))]

[JsonSerializable(typeof(TagItem))]
[JsonSerializable(typeof(Tag))]
[JsonSerializable(typeof(CreateTagRequest))]

[JsonSerializable(typeof(FavoriteResult))]

[JsonSerializable(typeof(TimelineGroup))]
[JsonSerializable(typeof(TimelineResponse))]
[JsonSerializable(typeof(DailyDensityItem))]
[JsonSerializable(typeof(YearCountItem))]
[JsonSerializable(typeof(MonthlyDensityItem))]
[JsonSerializable(typeof(DateIdsResponse))]
[JsonSerializable(typeof(DateIdItem))]
[JsonSerializable(typeof(List<DateIdItem>))]

[JsonSerializable(typeof(MapPhotoPoint))]
[JsonSerializable(typeof(ClusterPhotoItem))]
[JsonSerializable(typeof(ClusterResult))]
[JsonSerializable(typeof(ClustersResponse))]
[JsonSerializable(typeof(MapPointItem))]
[JsonSerializable(typeof(BasemapConfig))]

[JsonSerializable(typeof(FolderNode))]

[JsonSerializable(typeof(CreateUserRequest))]
[JsonSerializable(typeof(UpdateUserRequest))]
[JsonSerializable(typeof(UserListItem))]
[JsonSerializable(typeof(ThumbnailGenerationRequest))]
[JsonSerializable(typeof(FormatCountItem))]
[JsonSerializable(typeof(AdminStats))]

[JsonSerializable(typeof(IdsRequest))]
[JsonSerializable(typeof(ReadyResponse))]
[JsonSerializable(typeof(EnqueueResponse))]

[JsonSerializable(typeof(MoveRequest))]
[JsonSerializable(typeof(RenameRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]

[JsonSerializable(typeof(ScanStatus))]
[JsonSerializable(typeof(ScanLogItem))]
[JsonSerializable(typeof(ThumbnailStats))]
[JsonSerializable(typeof(ThumbnailGenerationStatus))]

[JsonSerializable(typeof(FsEntryDto))]
[JsonSerializable(typeof(FsBrowseResult))]
[JsonSerializable(typeof(List<FsEntryDto>))]

// List<T> variants used in controller responses (returned directly)
[JsonSerializable(typeof(List<PhotoItem>))]
[JsonSerializable(typeof(List<PhotoIdentity>))]
[JsonSerializable(typeof(List<PhotoListItem>))]
[JsonSerializable(typeof(List<TagItem>))]
[JsonSerializable(typeof(List<YearCountItem>))]
[JsonSerializable(typeof(List<MonthlyDensityItem>))]
[JsonSerializable(typeof(List<DailyDensityItem>))]
[JsonSerializable(typeof(List<FormatCountItem>))]
[JsonSerializable(typeof(List<ClusterResult>))]
[JsonSerializable(typeof(List<MapPointItem>))]
[JsonSerializable(typeof(List<FolderNode>))]
[JsonSerializable(typeof(List<ScanLogItem>))]
[JsonSerializable(typeof(List<UserListItem>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<MapPhotoPoint>))]

// Dictionary for admin settings update
[JsonSerializable(typeof(Dictionary<string, string>))]

// ASP.NET Core auto-converts 4xx responses to ProblemDetails
[JsonSerializable(typeof(ProblemDetails))]

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false
)]
public partial class GalleryCloudJsonContext : JsonSerializerContext
{
}
