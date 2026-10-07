using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

internal static class WallpaperEngine
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".jfif", ".tif", ".tiff",
    };

    // Formats WPF's MediaElement (Windows Media Foundation) can usually play on a stock
    // Windows 10/11 install. Exotic codecs (e.g. HEVC, some .mkv) fail gracefully: the
    // live host closes and the previous static wallpaper stays in place.
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mpg", ".mpeg", ".m2v", ".mkv", ".webm", ".3gp",
    };

    // Applies are serialized so two overlapping requests can never interleave, and each
    // request carries a generation number so a slow apply for an old profile can never
    // overwrite a newer activation.
    private static readonly SemaphoreSlim ApplyGate = new(1, 1);
    private static int _applyGeneration;

    public static bool IsImageFile(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Extensions.Contains(Path.GetExtension(path));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsVideoFile(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && VideoExtensions.Contains(Path.GetExtension(path));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A .gif used as a single-file profile can be animated on the live host.</summary>
    public static bool IsAnimatedGifFile(string path)
        => IsImageFile(path) && string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the profile source should play on the live wallpaper host (video or GIF).</summary>
    public static bool IsLiveFile(string path) => IsVideoFile(path) || IsAnimatedGifFile(path);

    /// <summary>Current apply generation. Applies capture this and are skipped if it moves on.</summary>
    public static int ApplyGeneration => Volatile.Read(ref _applyGeneration);

    /// <summary>Call when a new activation supersedes anything pending.</summary>
    public static int NextApplyGeneration() => Interlocked.Increment(ref _applyGeneration);

    public static List<string> GetImages(string folderPath)
    {
        var images = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return images;
            }
            if (File.Exists(folderPath) && IsImageFile(folderPath))
            {
                images.Add(folderPath);
                return images;
            }
            if (IsVideoFile(folderPath))
            {
                // A video is a live-wallpaper source, not a slideshow source.
                return images;
            }
            if (!Directory.Exists(folderPath))
            {
                Logger.Warn($"Wallpaper folder does not exist: '{folderPath}'");
                return images;
            }
            images.AddRange(Directory.EnumerateFiles(folderPath)
                .Where(f => Extensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Error($"Listing images in '{folderPath}' failed.", ex);
        }
        return images;
    }

    public static List<string> GetProfileMedia(WallpaperProfile profile)
    {
        var sources = GetImages(profile.FolderPath);
        if (File.Exists(profile.FolderPath) && IsVideoFile(profile.FolderPath)) sources.Add(profile.FolderPath);
        sources.AddRange(profile.AdditionalWallpaperPaths ?? new());
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                if (!File.Exists(source) || !(IsImageFile(source) || IsVideoFile(source))) continue;
                var path = Path.GetFullPath(source);
                if (seen.Add(path)) result.Add(path);
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }
        return result;
    }

    /// <summary>
    /// Synchronous apply for command-line use (no UI to block).
    /// </summary>
    public static bool SetWallpaper(string imagePath, FitMode fitMode)
        => SetWallpaperAsync(imagePath, fitMode, NextApplyGeneration()).GetAwaiter().GetResult();

    /// <summary>
    /// Applies in the background. Safe to call from the UI thread: all file IO and the
    /// Win32 call happen on a worker, requests are serialized, and requests older than
    /// <paramref name="generation"/> are skipped instead of applied out of order.
    /// </summary>
    public static Task<bool> SetWallpaperAsync(string imagePath, FitMode fitMode, int generation)
        => Task.Run(async () =>
        {
            await ApplyGate.WaitAsync();
            try
            {
                if (generation != ApplyGeneration)
                {
                    Logger.Info($"Skipped superseded wallpaper apply for '{imagePath}'.");
                    return false;
                }
                return SetWallpaperCore(imagePath, fitMode);
            }
            catch (Exception ex)
            {
                Logger.Error("Wallpaper apply failed unexpectedly.", ex);
                return false;
            }
            finally
            {
                ApplyGate.Release();
            }
        });

    /// <summary>
    /// Applies a wallpaper frame without the settings-change broadcast. Used by the
    /// icons-friendly live mode, which updates the real wallpaper several times a second:
    /// the desktop still repaints, but other apps are not spammed with change notifications.
    /// </summary>
    public static Task<bool> SetWallpaperFrameAsync(string imagePath, FitMode fitMode, int generation)
        => Task.Run(async () =>
        {
            await ApplyGate.WaitAsync();
            try
            {
                if (generation != ApplyGeneration)
                {
                    LogThrottled($"Skipped superseded live wallpaper frame '{imagePath}' (generation {generation} != {ApplyGeneration}).");
                    return false;
                }
                var source = Path.GetFullPath(imagePath);
                if (!File.Exists(source))
                {
                    LogThrottled($"Live wallpaper frame not found: '{source}'.");
                    return false;
                }
                ApplyFitToRegistry(fitMode);
                var ok = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0u, source, SPIF_UPDATEINIFILE);
                if (!ok)
                {
                    LogThrottled($"Applying live wallpaper frame failed (Win32 error {Marshal.GetLastWin32Error()}): '{source}'");
                }
                return ok;
            }
            catch (Exception ex)
            {
                LogThrottled($"Applying live wallpaper frame threw: {ex.Message}");
                return false;
            }
            finally
            {
                ApplyGate.Release();
            }
        });

    // The frame pusher fires several times a second — never let a persistent failure
    // flood the log; repeat the same diagnosis at most every few seconds.
    private static DateTime _lastFrameDiagLog;

    private static void LogThrottled(string message)
    {
        if ((DateTime.UtcNow - _lastFrameDiagLog).TotalSeconds < 3)
        {
            return;
        }
        _lastFrameDiagLog = DateTime.UtcNow;
        Logger.Warn(message);
    }

    private static bool SetWallpaperCore(string imagePath, FitMode fitMode)
    {
        try
        {
            var source = Path.GetFullPath(imagePath);
            if (!File.Exists(source))
            {
                Logger.Warn($"Cannot set wallpaper, image not found: '{source}'");
                return false;
            }
            var applyFrom = StageImage(source);
            ApplyFitToRegistry(fitMode);
            var ok = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0u, applyFrom, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            if (!ok && !string.Equals(applyFrom, source, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn($"Staged apply failed (Win32 error {Marshal.GetLastWin32Error()}); retrying from the original location.");
                ok = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0u, source, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            if (!ok)
            {
                Logger.Error($"Setting wallpaper failed (Win32 error {Marshal.GetLastWin32Error()}): '{source}'");
            }
            else
            {
                Logger.Info($"Wallpaper applied: '{source}' ({fitMode})");
            }
            return ok;
        }
        catch (Exception ex)
        {
            Logger.Error("Setting wallpaper threw an exception.", ex);
            return false;
        }
    }

    /// <summary>
    /// Copies the source image into a short local cache path and applies from there.
    /// This keeps the desktop wallpaper alive when the source is on a USB/network drive
    /// that later disconnects, sidesteps MAX_PATH truncation in the classic wallpaper
    /// API, and avoids exposing long/odd paths to the shell.
    /// </summary>
    private static string StageImage(string source)
    {
        try
        {
            var extension = Path.GetExtension(source);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".img";
            }
            Directory.CreateDirectory(AppPaths.CacheDir);
            var target = Path.Combine(AppPaths.CacheDir, "current" + extension);
            if (string.Equals(Path.GetFullPath(target), source, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
            var temp = Path.Combine(AppPaths.CacheDir, "staging" + extension);
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
            CleanupCache(target);
            return target;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Staging image to the local cache failed, applying from the original location: {ex.Message}");
            return source;
        }
    }

    private static void CleanupCache(string keepPath)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.CacheDir, "*.*"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("current.", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("staging.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(file, keepPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                    }
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static void ApplyFitToRegistry(FitMode fitMode)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true)
                      ?? Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", writable: true);
        var (style, tile) = fitMode.ToRegistryValues();
        key.SetValue("WallpaperStyle", style, RegistryValueKind.String);
        key.SetValue("TileWallpaper", tile, RegistryValueKind.String);
    }

    internal const uint SPI_SETDESKWALLPAPER = 0x0014;
    internal const uint SPIF_UPDATEINIFILE = 0x0001;
    internal const uint SPIF_SENDCHANGE = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);
}
