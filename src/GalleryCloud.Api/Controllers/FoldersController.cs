using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/folders")]
public class FoldersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;

    public FoldersController(AppDbContext db, UserContext userContext)
    {
        _db = db;
        _userContext = userContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetTree()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();
        var userId = _userContext.UserId;

        // Load all enabled roots for this user
        var roots = await _db.UserRoots
            .Where(r => r.UserId == userId && r.IsEnabled)
            .ToListAsync();

        // Load all user photo paths grouped by root
        var photos = await _db.Photos
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .Select(p => new { p.RootId, p.FilePath })
            .ToListAsync();

        var tree = new List<FolderNode>();
        foreach (var root in roots)
        {
            var rootPaths = photos.Where(p => p.RootId == root.Id).Select(p => p.FilePath).ToList();
            var rootName = Path.GetFileName(root.RootPath.TrimEnd(Path.DirectorySeparatorChar, '/', '\\'));

            tree.Add(new FolderNode
            {
                Name = rootName,
                Path = "",
                RootId = root.Id,
                PhotoCount = rootPaths.Count,
                SubFolders = BuildFolderTree(rootPaths, root.Id)
            });
        }

        return Ok(tree);
    }

    [HttpGet("{*path}")]
    public async Task<IActionResult> GetFolderPhotos(string path, [FromQuery] string? rootId = null)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        path = path?.Trim('/') ?? "";

        // Load all user photos then filter in memory — normalize separators for cross-platform
        var allPhotos = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted)
            .OrderByDescending(p => p.TakenAt).ThenBy(p => p.FileName)
            .Select(p => new PhotoListItem(
                p.Id, p.FileName, p.FileFormat,
                p.Width, p.Height, p.Orientation,
                p.TakenAt, null, null,
                p.FileSize, p.FilePath, null,
                null, p.RootId
            ))
            .ToListAsync();

        // Filter by root if specified
        if (!string.IsNullOrEmpty(rootId))
        {
            allPhotos = allPhotos.Where(p => p.RootId == rootId).ToList();
        }

        if (string.IsNullOrEmpty(path))
        {
            // Root level: return all photos (filtered by rootId above if given)
            return Ok(allPhotos);
        }

        var normalized = path.Replace('\\', '/').TrimEnd('/') + "/";
        var photos = allPhotos
            .Where(p => (p.FilePath ?? "").Replace('\\', '/').StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Ok(photos);
    }

    private static List<FolderNode> BuildFolderTree(List<string> paths, string rootId)
    {
        var root = new Dictionary<string, FolderNode>();

        foreach (var path in paths.OrderBy(p => p))
        {
            var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var folder = parts[0];
            if (!root.ContainsKey(folder))
                root[folder] = new FolderNode { Name = folder, Path = folder, RootId = rootId, SubFolders = new(), PhotoCount = 0 };

            root[folder].PhotoCount++;

            // Walk subfolder levels (i tracks depth into the path)
            var current = root[folder];
            for (int i = 1; i < parts.Length - 1; i++)
            {
                var subName = parts[i];
                var subPath = string.Join("/", parts.Take(i + 1));

                var existing = current.SubFolders.FirstOrDefault(f => f.Path == subPath);
                if (existing == null)
                {
                    existing = new FolderNode
                    {
                        Name = subName,
                        Path = subPath,
                        RootId = rootId,
                        SubFolders = new(),
                        PhotoCount = 0
                    };
                    current.SubFolders.Add(existing);
                }
                existing.PhotoCount++;
                current = existing;
            }
        }

        return root.Values.OrderBy(f => f.Name).ToList();
    }
}
