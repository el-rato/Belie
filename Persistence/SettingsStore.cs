using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Persistence;

internal sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var settings = AtomicJsonFile.Read<AppSettings>(_path);
                if (settings != null)
                {
                    settings.LastActiveProfileId ??= "";
                    settings.PreferredProfileId ??= "";
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read settings.json, using defaults.", ex);
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings) => AtomicJsonFile.Write(_path, settings);
}
