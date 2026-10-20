namespace WallpaperProfiles.Models;

internal sealed class WallpaperWidgetLayout
{
    public Guid ProfileId { get; set; }
    public string WallpaperPath { get; set; } = "";
}

internal sealed class WidgetLayoutPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<DesktopWidget> Widgets { get; set; } = new();
}

internal sealed class WidgetStylePreset
{
    public string Name { get; set; } = "";
    public DesktopWidget Style { get; set; } = new();
}

internal sealed class DesktopCanvasDocument
{
    public bool Enabled { get; set; }
    public Guid? ActiveProfileId { get; set; }
    public string ActiveWallpaperPath { get; set; } = "";
    public List<DesktopWidget> Widgets { get; set; } = new();
    public List<WallpaperWidgetLayout> WallpaperLayouts { get; set; } = new();
    public List<WidgetLayoutPreset> LayoutPresets { get; set; } = new();
    public List<WidgetStylePreset> StylePresets { get; set; } = new();

    public static bool SameWallpaper(string? left, string? right) =>
        string.Equals(left?.Replace('/', '\\'), right?.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    public bool HasLayout(Guid? profileId, string path) => profileId.HasValue && !string.IsNullOrEmpty(path)
        && WallpaperLayouts.Any(layout => layout.ProfileId == profileId && SameWallpaper(layout.WallpaperPath, path));

    public IEnumerable<DesktopWidget> Composition(Guid? profileId, string path, bool includeGlobal = true)
    {
        var scopedPath = HasLayout(profileId, path) ? path : "";
        return Widgets.Where(widget => widget.ProfileId == null
            ? includeGlobal && string.IsNullOrEmpty(widget.WallpaperPath)
            : widget.ProfileId == profileId && SameWallpaper(widget.WallpaperPath, scopedPath));
    }

    public IEnumerable<DesktopWidget> VisibleWidgets() => Composition(ActiveProfileId, ActiveWallpaperPath).Where(widget => widget.Enabled);

    public void CustomizeWallpaper(Guid profileId, string path)
    {
        if (HasLayout(profileId, path) || string.IsNullOrWhiteSpace(path)) return;
        var originals = Composition(profileId, "", false).ToArray();
        WallpaperLayouts.Add(new() { ProfileId = profileId, WallpaperPath = path });
        foreach (var original in originals)
        {
            var copy = original.Copy();
            copy.WallpaperPath = path; Widgets.Add(copy);
        }
    }

    public void ResetWallpaper(Guid profileId, string path)
    {
        Widgets.RemoveAll(widget => widget.ProfileId == profileId && SameWallpaper(widget.WallpaperPath, path));
        WallpaperLayouts.RemoveAll(layout => layout.ProfileId == profileId && SameWallpaper(layout.WallpaperPath, path));
    }
}
