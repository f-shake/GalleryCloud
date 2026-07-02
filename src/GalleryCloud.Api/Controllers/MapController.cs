using GalleryCloud.Api.Data;
using GalleryCloud.Api.Dtos;
using GalleryCloud.Api.Services;
using GalleryCloud.Core.Interfaces;
using GalleryCloud.Core.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GalleryCloud.Api.Controllers;

[ApiController]
[Route("api/map")]
public class MapController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserContext _userContext;

    public MapController(AppDbContext db, UserContext userContext)
    {
        _db = db;
        _userContext = userContext;
    }

    [HttpGet("clusters")]
    public async Task<IActionResult> GetClusters([FromQuery] int zoom = 10)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var points = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted)
                && p.Latitude != null && p.Longitude != null)
            .Select(p => new MapPhotoPoint(p.Id, p.FileName, p.Latitude!.Value, p.Longitude!.Value,
                p.TakenAt, p.Width, p.Height))
            .ToListAsync();

        var radius = GetClusterRadius(zoom);
        var clusters = ClusterPoints(points, radius);

        return Ok(new ClustersResponse(clusters, zoom));
    }

    [HttpGet("photos")]
    public async Task<IActionResult> GetNearby([FromQuery] double lat, [FromQuery] double lng,
        [FromQuery] double radius = 0.01)
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var photos = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted)
                && p.Latitude != null && p.Longitude != null
                && (p.Latitude!.Value - lat) * (p.Latitude!.Value - lat)
                    + (p.Longitude!.Value - lng) * (p.Longitude!.Value - lng) < radius * radius)
            .OrderBy(p => p.TakenAt)
            .Take(200)
            .Select(p => new PhotoItem(
                p.Id, p.FileName, p.FileFormat,
                p.Width, p.Height, p.Orientation,
                p.TakenAt, p.Latitude, p.Longitude, p.FileSize
            ))
            .ToListAsync();

        return Ok(photos);
    }

    [HttpGet("points")]
    public async Task<IActionResult> GetPoints()
    {
        if (!_userContext.IsAuthenticated) return Unauthorized();

        var points = await _db.Photos
            .Where(p => p.UserId == _userContext.UserId && !p.IsDeleted
                && _db.UserRoots.Any(r => r.Id == p.RootId && !r.IsDeleted)
                && p.Latitude != null && p.Longitude != null)
            .Select(p => new MapPointItem(
                p.Id, p.Latitude, p.Longitude, p.FileName, p.TakenAt
            ))
            .ToListAsync();

        return Ok(points);
    }

    [HttpGet("basemap-config")]
    public async Task<IActionResult> GetBasemapConfig([FromServices] ISettingService settings)
    {
        return Ok(new BasemapConfig(
            await settings.GetAsync(SettingKeys.MapTileUrlNormal,
                "https://tile.openstreetmap.org/{z}/{x}/{y}.png"),
            await settings.GetAsync(SettingKeys.MapTileUrlSatellite,
                "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}")
        ));
    }

    private static double GetClusterRadius(int z) => z switch
    {
        <= 5 => 5.0, <= 8 => 2.0, <= 10 => 0.5, <= 12 => 0.1, <= 14 => 0.02, _ => 0.005
    };

    private static List<ClusterResult> ClusterPoints(List<MapPhotoPoint> points, double radius)
    {
        var clusters = new List<ClusterResult>();
        var used = new HashSet<int>();

        for (int i = 0; i < points.Count; i++)
        {
            if (used.Contains(i)) continue;
            var group = new List<MapPhotoPoint> { points[i] };
            double latSum = points[i].Latitude, lngSum = points[i].Longitude;

            for (int j = i + 1; j < points.Count && group.Count < 500; j++)
            {
                if (used.Contains(j)) continue;
                var dlat = points[i].Latitude - points[j].Latitude;
                var dlng = points[i].Longitude - points[j].Longitude;
                if (dlat * dlat + dlng * dlng < radius * radius)
                {
                    group.Add(points[j]);
                    latSum += points[j].Latitude;
                    lngSum += points[j].Longitude;
                    used.Add(j);
                }
            }

            clusters.Add(new ClusterResult(
                latSum / group.Count,
                lngSum / group.Count,
                group.Count,
                group.Take(10).Select(p => new ClusterPhotoItem(p.Id, p.FileName, p.Width, p.Height)).ToList()
            ));
            used.Add(i);
        }

        return clusters;
    }
}
