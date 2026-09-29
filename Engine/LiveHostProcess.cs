using System.Diagnostics;
using System.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

/// <summary>
/// The live wallpaper can only render while a process is alive, so quitting the app
/// would normally kill it. Instead, on exit the app hands the currently playing
/// video/GIF over to a detached copy of itself running in <c>--live-host</c> mode,
/// which keeps playing behind the desktop icons until the app starts again (or the
/// user switches to a static wallpaper).
/// </summary>
internal static class LiveHostProcess
{
    public const string StopEventName = @"Local\Belie.LiveHost.Stop";

    /// <summary>
    /// Signals a running detached host to exit. Returns true when one was found.
    /// Waits briefly so a newly starting surface never overlaps the dying host's window.
    /// </summary>
    public static bool StopRunning()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(StopEventName, out var handle))
            {
                return false;
            }
            using (handle)
            {
                _ = handle.Set();
            }
            Logger.Info("Asked the detached live wallpaper host to stop.");
            Thread.Sleep(300); // give it a moment to tear its window down
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Signaling the live wallpaper host failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Starts a detached process that keeps the live wallpaper playing after exit.</summary>
    public static void Launch(string mediaPath, FitMode fitMode, bool muted)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(mediaPath))
            {
                return;
            }
            _ = StopRunning(); // never two hosts at once
            var args = $"--live-host \"{mediaPath}\" --fit {fitMode.ToString().ToLowerInvariant()}"
                       + (muted ? " --muted" : "");
            _ = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Logger.Info($"Detached live wallpaper host started for '{mediaPath}' ({fitMode}{(muted ? ", muted" : "")}).");
        }
        catch (Exception ex)
        {
            Logger.Error("Starting the detached live wallpaper host failed.", ex);
        }
    }
}
