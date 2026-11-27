using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Persistence;

/// <summary>How see-through Belie's window is. Off keeps solid surfaces.</summary>
internal enum GlassLevel { Off, Light, Strong }

/// <summary>Preferences for the app window itself, kept apart from the wallpaper settings in shell.json.</summary>
internal sealed class ShellSettings
{
    public GlassLevel Glass { get; set; } = GlassLevel.Strong;

    /// <summary>Keys that open the launcher while Belie's window is focused.</summary>
    public List<string> LauncherKeys { get; set; } = new() { "Ctrl+Space", "Ctrl+K" };

    public ShellSettings Normalize()
    {
        if (!Enum.IsDefined(Glass)) Glass = GlassLevel.Strong;
        LauncherKeys = LauncherKeys?.Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()).Distinct().ToList() ?? new();
        if (LauncherKeys.Count == 0) LauncherKeys = new() { "Ctrl+Space", "Ctrl+K" };
        return this;
    }
}

internal sealed class ShellStore
{
    public static ShellStore Default { get; } = new(Path.Combine(AppPaths.BaseDir, "shell.json"));

    public string FilePath { get; }
    public ShellStore(string path) => FilePath = Path.GetFullPath(path);

    public ShellSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && AtomicJsonFile.Read<ShellSettings>(FilePath) is { } settings) return settings.Normalize();
        }
        catch (Exception ex) { Logger.Error("Could not read the window settings; using defaults.", ex); }
        return new ShellSettings();
    }

    public void Save(ShellSettings settings) => AtomicJsonFile.Write(FilePath, settings.Normalize());
}
