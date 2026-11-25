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
                    if (!Enum.IsDefined(settings.WallpaperTransition)) settings.WallpaperTransition = WallpaperTransitionStyle.Automatic;
                    settings.TransitionSeconds = Clamp(settings.TransitionSeconds, .3, 6, 1.8);
                    settings.TransitionCell = Math.Clamp(settings.TransitionCell, 2, 48);
                    settings.TransitionGlowLength = Clamp(settings.TransitionGlowLength, .01, .4, .05);
                    settings.TransitionAngle = Clamp(settings.TransitionAngle, 0, 360, 30);
                    settings.TransitionDim = Clamp(settings.TransitionDim, 0, .9, .45);
                    settings.TransitionGlowColor ??= "";
                    settings.ChosenWallpapers ??= new();
                    if (!Enum.IsDefined(settings.SwitcherLayout)) settings.SwitcherLayout = SwitcherLayout.Slanted;
                    if (!Enum.IsDefined(settings.SwitcherBackdrop)) settings.SwitcherBackdrop = SwitcherBackdrop.None;
                    settings.SwitcherSize = Clamp(settings.SwitcherSize, .12, .7, .37);
                    settings.SwitcherSlant = Clamp(settings.SwitcherSlant, -35, 35, 18);
                    settings.SwitcherSpacing = Clamp(settings.SwitcherSpacing, -40, 80, 0);
                    settings.SwitcherRadius = Clamp(settings.SwitcherRadius, 0, 60, 0);
                    settings.SwitcherOutline = Clamp(settings.SwitcherOutline, 0, 6, 1.5);
                    settings.SwitcherPosition = Clamp(settings.SwitcherPosition, 0, 1, .5);
                    settings.SwitcherSpeed = Clamp(settings.SwitcherSpeed, 0, 4, 1);
                    settings.SwitcherSides = Math.Clamp(settings.SwitcherSides, 1, 5);
                    settings.SwitcherOutlineColor ??= "";
                    settings.SwitcherKeys ??= ""; settings.NextWallpaperKeys ??= ""; settings.PreviousWallpaperKeys ??= "";
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

    private static double Clamp(double value, double low, double high, double fallback) => double.IsFinite(value) ? Math.Clamp(value, low, high) : fallback;
}
