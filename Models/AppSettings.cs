namespace WallpaperProfiles.Models;

public class AppSettings
{
    public bool StartWithWindows { get; set; }

    public bool StartMinimizedToTray { get; set; }

    public string LastActiveProfileId { get; set; } = "";

    public string PreferredProfileId { get; set; } = "";
}
