namespace WallpaperProfiles.Models;

public class WallpaperProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string KeyboardShortcut { get; set; } = "";

    public string FolderPath { get; set; } = "";

    public List<string> AdditionalWallpaperPaths { get; set; } = new();

    public FitMode FitMode { get; set; } = FitMode.Fill;

    public int SlideshowIntervalMinutes { get; set; }

    public bool SlideshowRandom { get; set; }

    /// <summary>Mutes the audio of a live (video) wallpaper. Ignored for static images.</summary>
    public bool VideoMuted { get; set; } = true;

    /// <summary>
    /// Plays live wallpapers by updating the actual desktop wallpaper at a reduced frame
    /// rate instead of using the overlay window — keeps the desktop icons and taskbar
    /// visible on Windows builds where the overlay is the only rendering option.
    /// </summary>
    public bool IconFriendlyLive { get; set; }

    public string SceneAccent { get; set; } = "";

    public string AmbientAudioPath { get; set; } = "";

    public int AmbientVolume { get; set; } = 30;

    public bool AmbientMuted { get; set; }

    public List<ScheduleRule> Schedule { get; set; } = new();

    public List<EventTrigger> EventTriggers { get; set; } = new();
}

internal static class ScenePresets
{
    public static readonly string[] Names = { "Focus", "Gaming", "Evening" };

    public static WallpaperProfile Create(string name, string wallpaperPath = "") => name switch
    {
        "Focus" => new WallpaperProfile { Name = name, FolderPath = wallpaperPath, SceneAccent = "#B7CDBC", AmbientVolume = 25 },
        "Gaming" => new WallpaperProfile { Name = name, FolderPath = wallpaperPath, SceneAccent = "#ADBDF4", AmbientVolume = 15, AmbientMuted = true },
        "Evening" => new WallpaperProfile { Name = name, FolderPath = wallpaperPath, SceneAccent = "#D8B995", AmbientVolume = 35 },
        _ => throw new ArgumentException("Unknown starter scene.", nameof(name))
    };
}
