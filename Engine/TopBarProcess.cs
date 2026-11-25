using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Workspaces;

namespace WallpaperProfiles.Engine;

internal static class TopBarProcess
{
    public const string HostArgument = "--topbar-host";
    public const string GuardArgument = "--workspace-guard";
    /// <summary>Signaled by the bar when a workspace with a profile opens on the primary monitor.</summary>
    public const string ProfileEventName = @"Local\Belie.TopBar.Profile";
    /// <summary>Signaled when the bar asks the tray app to open the wallpaper switcher, or to change the wallpaper.</summary>
    public static string WallpaperEventName(WallpaperAction action) => @"Local\Belie.Wallpaper." + action;
    public const string SettingsEventName = @"Local\Belie.TopBar.Settings";

    public static string DefaultFile => Path.Combine(AppPaths.BaseDir, "topbar.json");
    public static string ProfileRequestFile(string file) => Path.ChangeExtension(Path.GetFullPath(file), ".profile.json");
    public static string MutexName(string file) => @"Local\Belie.TopBar." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..16];

    public static bool IsRunning(string file)
    {
        if (!Mutex.TryOpenExisting(MutexName(file), out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    public static bool IsWanted(string file) => new TopBarStore(file).Load().Enabled;

    public static void Start(string file)
    {
        if (IsRunning(file)) return;
        if (!IsWanted(file))
        {
            // A previous host may have ended without bringing its windows back.
            if (File.Exists(HiddenWindowJournal.PathFor(file)))
                new HiddenWindowJournal(HiddenWindowJournal.PathFor(file)).RestoreOrphans(new NativeWindowSystem());
            return;
        }
        if (DesktopHosts.RequestAttach(DesktopHosts.CanvasFileFor(file), file)) return;
        DetachedProcess.Start(HostArgument, Path.GetFullPath(file));
    }

    public static void Stop(string file)
    {
        if (!EventWaitHandle.TryOpenExisting(MutexName(file) + ".Stop", out var stop)) return;
        using (stop) stop.Set();
    }

    public static string CommandFile(string file) => Path.ChangeExtension(Path.GetFullPath(file), ".command.json");

    /// <summary>
    /// A profile shortcut can be the same keys as a workspace shortcut, and Windows gives the keys to whoever asked
    /// first. This opens the workspace on the bar's behalf. Returns the profile to apply, or null when the keys
    /// are not a workspace shortcut.
    /// </summary>
    public static Guid? ForwardShortcut(string file, string shortcut, Guid profile)
    {
        if (!ProfileShortcutManager.TryParse(shortcut, out var gesture, out _)
            || !EventWaitHandle.TryOpenExisting(MutexName(file) + ".Command", out var command)) return null;
        using (command)
        {
            var settings = new TopBarStore(file).Load();
            if (settings.WorkspaceFor(gesture!) is not { } index) return null;
            AtomicJsonFile.Write(CommandFile(file), new WorkspaceCommand(index));
            command.Set();
            return settings.WorkspaceProfiles.GetValueOrDefault(index, profile);
        }
    }

    /// <summary>Starts the small process that brings hidden windows back if the bar crashes or is ended.</summary>
    public static void StartGuard(string file)
    {
        using var current = Process.GetCurrentProcess();
        DetachedProcess.Start(GuardArgument, current.Id.ToString(CultureInfo.InvariantCulture), Path.GetFullPath(file));
    }

    /// <summary>The process can outlive the bar, because the desktop widgets share it.</summary>
    private static void WaitForBar(Process host, string file)
    {
        if (!EventWaitHandle.TryOpenExisting(ClosedEventName(file), out var closed)) { host.WaitForExit(); return; }
        using (closed)
        using (var ended = new EventWaitHandle(false, EventResetMode.ManualReset))
        {
            ended.SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(host.Handle, ownsHandle: false);
            WaitHandle.WaitAny(new WaitHandle[] { ended, closed });
        }
    }

    internal static string ClosedEventName(string file) => MutexName(file) + ".Closed";

    /// <summary>Runs the guard when the command line asks for it. It uses no windows and costs little memory.</summary>
    public static bool RunGuard(IReadOnlyList<string> args)
    {
        if (args.Count < 3 || !args[0].Equals(GuardArgument, StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return true;
        Logger.Init(AppPaths.LogsDir);
        try
        {
            var file = args[2];
            try
            {
                using var host = Process.GetProcessById(id);
                // The host holds its mutex while it runs, so without it this is a different process reusing the id.
                if (IsRunning(file)) WaitForBar(host, file);
            }
            catch (ArgumentException) { } // Already gone.
            // A replacement host has already brought the windows back and may have hidden its own.
            if (!IsRunning(file)) new HiddenWindowJournal(HiddenWindowJournal.PathFor(file)).RestoreOrphans(new NativeWindowSystem());
        }
        catch (Exception ex) { Logger.Error("Restoring hidden windows failed.", ex); }
        return true;
    }
}
