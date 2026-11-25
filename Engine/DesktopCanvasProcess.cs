using System.Security.Cryptography;
using System.Text;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Persistence;

namespace WallpaperProfiles.Engine;

internal static class DesktopCanvasProcess
{
    public static string DefaultFile => Path.Combine(AppPaths.BaseDir, "desktop-canvas.json");
    public static string MutexName(string file) => @"Local\Belie.Canvas." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..16];
    public static bool IsRunning(string file)
    {
        if (!Mutex.TryOpenExisting(MutexName(file), out var mutex)) return false;
        mutex.Dispose();
        return true;
    }
    public static bool IsWanted(string file)
    {
        var document = new DesktopCanvasStore(file).Load();
        return document.Enabled && document.Widgets.Any(w => w.Enabled);
    }
    public static void Start(string file)
    {
        if (!IsWanted(file) || IsRunning(file) || DesktopHosts.RequestAttach(file, DesktopHosts.BarFileFor(file))) return;
        DetachedProcess.Start("--widget-host", "--canvas-file", Path.GetFullPath(file));
    }
    public static void Stop(string file)
    {
        if (!EventWaitHandle.TryOpenExisting(MutexName(file) + ".Stop", out var stop)) return;
        using (stop) stop.Set();
    }
}
