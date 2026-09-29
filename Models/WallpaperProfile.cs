namespace WallpaperProfiles.Models;

public class WallpaperProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string FolderPath { get; set; } = "";

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

    public List<ScheduleRule> Schedule { get; set; } = new();

    public List<EventTrigger> EventTriggers { get; set; } = new();
}
