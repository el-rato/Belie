using Microsoft.Win32;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Autostart;

internal static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Belie";
    private const string LegacyValueName = "WallpaperProfiles";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            Logger.Error("Reading autostart registry key failed.", ex);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    throw new InvalidOperationException("Executable path is not available.");
                }
                key.SetValue(ValueName, "\"" + exe + "\"", RegistryValueKind.String);
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false); // clean up the old name
                Logger.Info($"Autostart enabled for: {exe}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("Autostart disabled.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Changing autostart setting failed.", ex);
        }
    }
}
