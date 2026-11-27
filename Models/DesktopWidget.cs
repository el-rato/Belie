namespace WallpaperProfiles.Models;

internal enum DesktopWidgetKind { Note, Countdown, Image, Link, Sketch, NowPlaying, Clock, Visualizer, System, Weather, Calendar, Checklist, Timer }
internal enum ClockSkin { Script, Editorial, Minimal, Stacked, Analog, Digital, Terminal, Split, Words, DateCard }
internal enum SpectrumSkin { Mirrored, Bars, Wave, Line, Dots, Ring, Arc, Radial }
internal enum NowPlayingLayout { Horizontal, Stacked, Minimal, Cover, Vinyl, Ring, Pill }
internal enum SystemSkin { Rings, Bars, Graph, Compact }
internal enum WeatherSkin { Compact, Details, Forecast }
[Flags] internal enum SystemMetrics { None = 0, Cpu = 1, Memory = 2, Disk = 4, Network = 8, Battery = 16, Uptime = 32 }
internal enum WidgetTextAlignment { Left, Center, Right }
internal enum WidgetImageFit { Fit, Fill, Stretch }

/// <summary>Where a widget sits while one particular wallpaper is on the desktop.</summary>
internal sealed class WidgetPlacement
{
    public string WallpaperPath { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public WidgetPlacement Clone() => (WidgetPlacement)MemberwiseClone();
}

/// <summary>A wallpaper, within one profile, where a widget is kept off the desktop.</summary>
internal sealed class WidgetHiddenSpot
{
    public string WallpaperPath { get; set; } = "";
    /// <summary>The profile the widget is hidden in; null hides it on that wallpaper in every profile.</summary>
    public Guid? ProfileId { get; set; }
    public WidgetHiddenSpot Clone() => (WidgetHiddenSpot)MemberwiseClone();
}

internal sealed class DesktopWidget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DesktopWidgetKind Kind { get; set; }
    public string Title { get; set; } = "New note";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled " + Kind.ToString().ToLowerInvariant() : Title;
    public string Content { get; set; } = "";
    public DateTimeOffset Target { get; set; } = DateTimeOffset.Now.AddDays(7);
    public double X { get; set; } = 160;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 230;
    public bool Enabled { get; set; } = true;
    public Guid? ProfileId { get; set; }
    /// <summary>Legacy (Belie 3.4.2): set only on old per-wallpaper copies until they're migrated.</summary>
    public string WallpaperPath { get; set; } = "";
    /// <summary>Spots for wallpapers where the widget was moved. Other wallpapers use X, Y, Width and Height.</summary>
    public List<WidgetPlacement> Placements { get; set; } = new();

    public WidgetPlacement? PlacementFor(string? wallpaperPath) => string.IsNullOrEmpty(wallpaperPath) ? null
        : Placements.FirstOrDefault(p => DesktopCanvasDocument.SameWallpaper(p.WallpaperPath, wallpaperPath));

    /// <summary>A view of this widget (same Id) positioned for <paramref name="wallpaperPath"/>.</summary>
    public DesktopWidget PlacedOn(string? wallpaperPath)
    {
        var placed = (DesktopWidget)MemberwiseClone();
        if (PlacementFor(wallpaperPath) is { } spot) { placed.X = spot.X; placed.Y = spot.Y; placed.Width = spot.Width; placed.Height = spot.Height; }
        return placed;
    }

    /// <summary>Moves the widget on one wallpaper, or its default spot when <paramref name="wallpaperPath"/> is empty.</summary>
    public void Place(string? wallpaperPath, double x, double y, double width, double height)
    {
        if (string.IsNullOrEmpty(wallpaperPath)) { X = x; Y = y; Width = width; Height = height; return; }
        var spot = PlacementFor(wallpaperPath);
        if (spot == null) Placements.Add(spot = new WidgetPlacement { WallpaperPath = wallpaperPath });
        spot.X = x; spot.Y = y; spot.Width = width; spot.Height = height;
    }

    /// <summary>Puts the widget back on its default spot for one wallpaper.</summary>
    public bool ResetPlacement(string? wallpaperPath) =>
        Placements.RemoveAll(p => DesktopCanvasDocument.SameWallpaper(p.WallpaperPath, wallpaperPath)) > 0;

    /// <summary>Wallpapers where the widget is hidden. It still shows on every other wallpaper.</summary>
    public List<WidgetHiddenSpot> HiddenOn { get; set; } = new();

    public bool IsHiddenOn(Guid? profileId, string? wallpaperPath) => !string.IsNullOrEmpty(wallpaperPath)
        && HiddenOn.Any(h => DesktopCanvasDocument.SameWallpaper(h.WallpaperPath, wallpaperPath) && (h.ProfileId == null || h.ProfileId == profileId));

    /// <summary>Hides or shows the widget on one wallpaper of one profile. Returns true when anything changed.</summary>
    public bool SetHiddenOn(Guid? profileId, string? wallpaperPath, bool hidden)
    {
        if (string.IsNullOrEmpty(wallpaperPath)) return false;
        if (!hidden)
            return HiddenOn.RemoveAll(h => DesktopCanvasDocument.SameWallpaper(h.WallpaperPath, wallpaperPath) && (h.ProfileId == null || h.ProfileId == profileId)) > 0;
        if (IsHiddenOn(profileId, wallpaperPath)) return false;
        HiddenOn.Add(new WidgetHiddenSpot { WallpaperPath = wallpaperPath, ProfileId = profileId });
        return true;
    }

    public bool IsVisibleOn(Guid? activeProfileId) => Enabled && (!ProfileId.HasValue || ProfileId == activeProfileId);
    public bool Locked { get; set; }
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 16;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public WidgetTextAlignment Alignment { get; set; }
    public string TextColor { get; set; } = "#F1F2EE";
    public string BackgroundColor { get; set; } = "#1D201E";
    public double Opacity { get; set; } = 1;
    public double CornerRadius { get; set; } = 12;
    public bool ShowHeader { get; set; } = true;
    public bool ShowBorder { get; set; } = true;
    public string BorderColor { get; set; } = "#738579";
    public double BorderWidth { get; set; } = 1;
    public bool ShowBackground { get; set; } = true;
    public bool GlassEffect { get; set; }
    public NowPlayingLayout MusicLayout { get; set; }
    public bool ShowAlbumArt { get; set; } = true;
    public bool ShowPlaybackControls { get; set; } = true;
    public bool ShowPlaybackProgress { get; set; } = true;
    public bool ShowAudioVisualizer { get; set; }
    public string MusicAccentColor { get; set; } = "#A9C9B4";
    /// <summary>Takes the playback accent from the album art.</summary>
    public bool MusicColorsFromArt { get; set; }
    /// <summary>Shows the album art, blurred, behind the player.</summary>
    public bool MusicArtBackdrop { get; set; }
    public SystemSkin SystemSkin { get; set; }
    public SystemMetrics SystemMetrics { get; set; } = SystemMetrics.Cpu | SystemMetrics.Memory | SystemMetrics.Disk;
    public WeatherSkin WeatherSkin { get; set; } = WeatherSkin.Forecast;
    public bool WeatherFahrenheit { get; set; }
    public int TimerMinutes { get; set; } = 25;
    public int TimerBreakMinutes { get; set; } = 5;
    public bool ClockUse24Hour { get; set; }
    public bool ClockShowGreeting { get; set; } = true;
    public bool ClockShowVisualizer { get; set; } = true;
    public ClockSkin ClockSkin { get; set; }
    public SpectrumSkin SpectrumSkin { get; set; }
    public int SpectrumDensity { get; set; } = 48;
    public double SpectrumThickness { get; set; } = 2;
    public double SpectrumSensitivity { get; set; } = 1;
    public double SpectrumSmoothing { get; set; } = .65;
    public WidgetImageFit ImageFit { get; set; }
    public string Drawing { get; set; } = "";
    public double DrawingWidth { get; set; } = 300;
    public double DrawingHeight { get; set; } = 180;
    public string LinkDescription { get; set; } = "";
    public string LinkCustomDescription { get; set; } = "";
    public string LinkPageTitle { get; set; } = "";
    public string LeetCodeUsername { get; set; } = "";
    public List<DateOnly> PotdCompletions { get; set; } = new();
    public string PotdTitle { get; set; } = "";
    public string PotdUrl { get; set; } = "";
    public DateOnly? PotdDate { get; set; }
    public int ActivityStreak { get; set; }
    public DateTimeOffset? LinkUpdatedAt { get; set; }
    public string LinkStatus { get; set; } = "";

    /// <summary>Copies a widget into another composition without changing its appearance or position.</summary>
    public DesktopWidget Copy()
    {
        var copy = (DesktopWidget)MemberwiseClone();
        copy.PotdCompletions = new(PotdCompletions);
        copy.Placements = Placements.Select(p => p.Clone()).ToList();
        copy.HiddenOn = HiddenOn.Select(h => h.Clone()).ToList();
        copy.Id = Guid.NewGuid();
        return copy;
    }

    public DesktopWidget Duplicate()
    {
        var copy = Copy();
        if (!string.IsNullOrWhiteSpace(copy.Title)) copy.Title += " copy";
        copy.X += 24; copy.Y += 24;
        foreach (var spot in copy.Placements) { spot.X += 24; spot.Y += 24; }
        return copy;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLeetCode => TryGetLink(Content, out var link)
        && (link!.Host.Equals("leetcode.com", StringComparison.OrdinalIgnoreCase) || link.Host.Equals("www.leetcode.com", StringComparison.OrdinalIgnoreCase));
    public int PotdStreak(DateOnly today)
    {
        var completed = PotdCompletions.ToHashSet();
        var day = completed.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (completed.Contains(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    public static bool TryGetLink(string text, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)) return false;
        uri = parsed;
        return true;
    }

    public string CountdownText(DateTimeOffset now)
    {
        var remaining = Target - now;
        if (remaining <= TimeSpan.Zero) return "It’s time";
        return remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours:00}h {remaining.Minutes:00}m"
            : $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}
