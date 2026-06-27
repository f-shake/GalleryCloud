using GalleryCloud.Api.Data;
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

        var photos = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && (p.FilePath.StartsWith(path + "/") || p.FilePath.StartsWith(path + "\\")))
            .OrderBy(p => p.FileName)
            .Select(p => new
            {
                p.Id, p.FileName, p.FileFormat, p.FilePath,
                p.Width, p.Height, p.Orientation,
                p.TakenAt, p.FileSize
            })
            .ToListAsync();

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

    public class FolderNode
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public int PhotoCount { get; set; }
        public List<FolderNode> SubFolders { get; set; } = new();
    }
}
