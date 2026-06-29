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

        var photos = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted)
            .Select(p => p.FilePath)
            .ToListAsync();

        var tree = BuildTree(photos);
        return Ok(tree);
    }

    [HttpGet("{*path}")]
    public async Task<IActionResult> GetFolderPhotos(string path)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        path = path?.Trim('/') ?? "";

        // Load all user photos then filter in memory — normalize separators for cross-platform
        var allPhotos = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted)
            .OrderBy(p => p.FileName)
            .Select(p => new PhotoListItem(
                p.Id, p.FileName, p.FileFormat,
                p.Width, p.Height, p.Orientation,
                p.TakenAt, null, null,
                p.FileSize, p.FilePath, null
            ))
            .ToListAsync();

        var normalized = path.Replace('\\', '/').TrimEnd('/') + "/";
        var photos = allPhotos
            .Where(p => (p.FilePath ?? "").Replace('\\', '/').StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Ok(photos);
    }

    private static List<FolderNode> BuildTree(List<string> paths)
    {
        var root = new Dictionary<string, FolderNode>();

        foreach (var path in paths.OrderBy(p => p))
        {
            var parts = path.Replace('\\', '/').Split('/');
            if (parts.Length < 2) continue;

            var folder = parts[0];
            if (!root.ContainsKey(folder))
                root[folder] = new FolderNode { Name = folder, Path = folder, SubFolders = new(), PhotoCount = 0 };

            root[folder].PhotoCount++;

            for (int i = 1; i < parts.Length - 1; i++)
            {
                var current = root[folder];
                for (int j = 1; j <= i; j++)
                {
                    var subPath = string.Join("/", parts.Take(j + 1));
                    if (!current.SubFolders.Any(f => f.Path == subPath))
                    {
                        current.SubFolders.Add(new FolderNode
                        {
                            Name = parts[j],
                            Path = subPath,
                            SubFolders = new(),
                            PhotoCount = 0
                        });
                    }
                    var child = current.SubFolders.First(f => f.Path == subPath);
                    child.PhotoCount++;
                    current = child;
                }
            }
        }

        return root.Values.OrderBy(f => f.Name).ToList();
    }
}
