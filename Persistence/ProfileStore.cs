using System.Text.Json;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Persistence;

internal sealed class ProfileStore
{
    private readonly string _dir;

    public ProfileStore(string dir) => _dir = dir;

    public List<WallpaperProfile> LoadAll()
    {
        var result = new List<WallpaperProfile>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var profile = JsonSerializer.Deserialize<WallpaperProfile>(stream, JsonOptions.Shared);
                    if (profile == null || profile.Id == Guid.Empty)
                    {
                        continue;
                    }
                    Normalize(profile);
                    result.Add(profile);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Could not read profile '{file}', skipping.", ex);
                    TryQuarantine(file);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Reading profiles folder failed.", ex);
        }
        return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Save(WallpaperProfile profile)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, profile.Id + ".json");
        var temp = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(profile, JsonOptions.Shared);
            File.WriteAllText(temp, json);
            // Atomic replace: readers never observe a half-written file, and a crash
            // mid-write leaves the previous profile intact instead of corrupting it.
            File.Move(temp, path, overwrite: true);
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
        Logger.Info($"Saved profile '{profile.Name}' -> {path}");
    }

    public bool Delete(Guid id)
    {
        var path = Path.Combine(_dir, id + ".json");
        if (!File.Exists(path))
        {
            return false;
        }
        File.Delete(path);
        return true;
    }

    private static void Normalize(WallpaperProfile profile)
    {
        profile.Name ??= "";
        profile.FolderPath ??= "";
        profile.SceneAccent ??= "";
        profile.AmbientAudioPath ??= "";
        profile.AmbientVolume = Math.Clamp(profile.AmbientVolume, 0, 100);
        profile.SlideshowIntervalMinutes = Math.Clamp(profile.SlideshowIntervalMinutes, 0, 1440);
        profile.Schedule ??= new List<ScheduleRule>();
        profile.EventTriggers ??= new List<EventTrigger>();
        foreach (var rule in profile.Schedule)
        {
            rule.DaysOfWeek ??= new HashSet<DayOfWeek>();
            if (rule.Id == Guid.Empty)
            {
                rule.Id = Guid.NewGuid();
            }
            if (rule.CreatedAtUtc == default)
            {
                rule.CreatedAtUtc = DateTime.UtcNow;
            }
        }
        foreach (var trigger in profile.EventTriggers)
        {
            if (trigger.Id == Guid.Empty)
            {
                trigger.Id = Guid.NewGuid();
            }
            trigger.Condition ??= "";
        }
    }

    private static void TryQuarantine(string file)
    {
        try
        {
            File.Move(file, file + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        }
        catch
        {
        }
    }
}
