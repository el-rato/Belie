using System.Text.Json;
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
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions.Shared);
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

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions.Shared));
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // best effort cleanup
            }
            throw;
        }
    }
}
