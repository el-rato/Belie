namespace WallpaperProfiles.Infrastructure;

internal static class AppPaths
{
    private const string AppFolderName = "Belie";
    private const string LegacyFolderName = "WallpaperProfiles";

    /// <summary>Set to a folder to run a test build against its own copy of the data, away from the real setup.</summary>
    public const string DevDataVariable = "BELIE_DATA_DIR";

    /// <summary>True when <see cref="DevDataVariable"/> points this run (and its helper processes) at a separate data folder.</summary>
    public static bool IsDevBuild { get; } = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DevDataVariable));

    public static string BaseDir { get; } = IsDevBuild
        ? Path.GetFullPath(Environment.GetEnvironmentVariable(DevDataVariable)!)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string ProfilesDir => Path.Combine(BaseDir, "profiles");

    public static string LogsDir => Path.Combine(BaseDir, "logs");

    public static string CacheDir => Path.Combine(BaseDir, "cache");

    public static string SettingsFile => Path.Combine(BaseDir, "settings.json");

    public static string AppDataRoot => BaseDir;

    public static void EnsureDirectories()
    {
        MigrateLegacyData();
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(CacheDir);
    }

    /// <summary>
    /// One-time carry-over from the app's previous identity ("WallpaperProfiles"):
    /// profiles and settings move to the new folder so nobody loses their setup.
    /// </summary>
    private static void MigrateLegacyData()
    {
        if (IsDevBuild) return;
        try
        {
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyFolderName);
            if (!Directory.Exists(legacy))
            {
                return;
            }
            var haveNewData = Directory.Exists(ProfilesDir) && File.Exists(SettingsFile);
            if (haveNewData)
            {
                return; // already migrated (or fresh start) — leave the old folder alone
            }
            Directory.CreateDirectory(ProfilesDir);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(legacy, "profiles")))
            {
                File.Copy(file, Path.Combine(ProfilesDir, Path.GetFileName(file)), overwrite: false);
            }
            var legacySettings = Path.Combine(legacy, "settings.json");
            if (File.Exists(legacySettings))
            {
                File.Copy(legacySettings, SettingsFile, overwrite: false);
            }
            Logger.Info($"Migrated profile data from '{legacy}' to '{BaseDir}'.");
        }
        catch (Exception ex)
        {
            // Migration is best effort; the app works fine with a fresh folder.
            Logger.Warn($"Migrating legacy profile data failed: {ex.Message}");
        }
    }
}
