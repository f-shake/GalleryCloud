namespace GalleryCloud.Core.Dtos;

public record FsEntryDto(string Name, string FullPath, bool IsDrive);

public record FsBrowseResult(string CurrentPath, List<FsEntryDto> Entries, string? ParentPath, bool IsRoot);
