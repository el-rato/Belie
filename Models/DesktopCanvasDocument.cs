namespace WallpaperProfiles.Models;

/// <summary>Legacy (Belie 3.4.2): a wallpaper that had its own copy of a profile's widgets. Read only to migrate.</summary>
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
    /// <summary>Legacy; always empty after <see cref="MigrateWallpaperLayouts"/>.</summary>
    public List<WallpaperWidgetLayout> WallpaperLayouts { get; set; } = new();
    public List<WidgetLayoutPreset> LayoutPresets { get; set; } = new();
    public List<WidgetStylePreset> StylePresets { get; set; } = new();

    public static bool SameWallpaper(string? left, string? right) =>
        string.Equals(left?.Replace('/', '\\'), right?.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>The widgets shown for a profile: its own widgets plus, optionally, the ones for every profile.</summary>
    public IEnumerable<DesktopWidget> Composition(Guid? profileId, bool includeGlobal = true) =>
        Widgets.Where(widget => widget.ProfileId == null ? includeGlobal : widget.ProfileId == profileId);

    /// <summary>Enabled widgets for the active profile, each at its spot for the active wallpaper.</summary>
    public IEnumerable<DesktopWidget> VisibleWidgets() =>
        Composition(ActiveProfileId).Where(widget => widget.Enabled).Select(widget => widget.PlacedOn(ActiveWallpaperPath));

    /// <summary>
    /// Folds the old per-wallpaper widget copies back into the widget they were copied from. The copy's spot
    /// becomes that widget's spot on the wallpaper, so every wallpaper shows the widget again.
    /// Returns true when anything changed.
    /// </summary>
    public bool MigrateWallpaperLayouts()
    {
        var copies = Widgets.Where(widget => !string.IsNullOrEmpty(widget.WallpaperPath)).ToList();
        if (copies.Count == 0 && WallpaperLayouts.Count == 0) return false;
        var claimed = new HashSet<(Guid, string)>();
        foreach (var copy in copies)
        {
            var path = copy.WallpaperPath;
            var original = Widgets.FirstOrDefault(widget => string.IsNullOrEmpty(widget.WallpaperPath)
                && widget.ProfileId == copy.ProfileId && widget.Kind == copy.Kind && widget.Title == copy.Title
                && widget.Content == copy.Content && !claimed.Contains((widget.Id, path.ToUpperInvariant())));
            if (original == null)
            {
                // Made only for that wallpaper: keep it as a profile widget at the same spot.
                copy.WallpaperPath = "";
                continue;
            }
            claimed.Add((original.Id, path.ToUpperInvariant()));
            if (copy.X != original.X || copy.Y != original.Y || copy.Width != original.Width || copy.Height != original.Height)
                original.Place(path, copy.X, copy.Y, copy.Width, copy.Height);
            Widgets.Remove(copy);
        }
        WallpaperLayouts.Clear();
        return true;
    }
}
