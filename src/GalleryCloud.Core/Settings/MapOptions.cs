namespace GalleryCloud.Core.Settings;

public class MapOptions
{
    public string TileUrlNormal { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public string TileUrlSatellite { get; set; } = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}";
    public string DefaultBasemap { get; set; } = "normal";  // normal / satellite
}
