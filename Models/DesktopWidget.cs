namespace WallpaperProfiles.Models;

internal enum DesktopWidgetKind { Note, Countdown, Image, Link, Sketch, NowPlaying, Clock, Visualizer }
internal enum ClockSkin { Script, Editorial, Minimal, Stacked, Analog, Digital, Terminal, Split, Words, DateCard }
internal enum SpectrumSkin { Mirrored, Bars, Wave, Line, Dots, Ring, Arc, Radial }
internal enum NowPlayingLayout { Horizontal, Stacked, Minimal }
internal enum WidgetTextAlignment { Left, Center, Right }
internal enum WidgetImageFit { Fit, Fill, Stretch }

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
    public string WallpaperPath { get; set; } = "";
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
        copy.Id = Guid.NewGuid();
        return copy;
    }

    public DesktopWidget Duplicate()
    {
        var copy = Copy();
        if (!string.IsNullOrWhiteSpace(copy.Title)) copy.Title += " copy";
        copy.X += 24; copy.Y += 24;
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
