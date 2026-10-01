namespace WallpaperProfiles.Models;

internal sealed class WallpaperAsset
{
    public string FilePath { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFavorite { get; set; }
    public string Collection { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}
