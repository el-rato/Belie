using System.Runtime.InteropServices;
using System.Text;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Workspaces;

internal sealed class NativeWindowSystem : IWindowSystem
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "MultitaskingViewFrame", "ForegroundStaging", "TaskListThumbnailWnd"
    };
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Belie's own windows restore themselves from the tray, which cannot see that a workspace hid them.
        "Belie", "SearchHost", "SearchApp", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "LockApp", "ScreenClippingHost"
    };

    private readonly Func<IReadOnlyCollection<string>> _pinnedApps;
    private readonly Func<bool> _primaryOnly;
    private readonly Func<DesktopWindow, bool>? _only;
    private readonly Func<bool> _stickyPopouts;

    /// <param name="only">Limits which windows may ever be touched; used when trying workspaces on test windows.</param>
    /// <param name="stickyPopouts">Picture-in-picture players and call windows stay on every workspace.</param>
    public NativeWindowSystem(Func<IReadOnlyCollection<string>>? pinnedApps = null, Func<bool>? primaryOnly = null, Func<DesktopWindow, bool>? only = null,
        Func<bool>? stickyPopouts = null)
    {
        _pinnedApps = pinnedApps ?? (() => Array.Empty<string>());
        _primaryOnly = primaryOnly ?? (() => false);
        _only = only;
        _stickyPopouts = stickyPopouts ?? (() => false);
    }

    public IReadOnlyList<DesktopWindow> VisibleWindows()
    {
        var pinned = new HashSet<string>(_pinnedApps(), StringComparer.OrdinalIgnoreCase);
        var handles = new List<IntPtr>();
        NativeMethods.EnumWindows((window, _) =>
        {
            if (NativeMethods.IsWindowVisible(window) && !IsCloaked(window)) handles.Add(window);
            return true;
        }, IntPtr.Zero);
        var found = new Dictionary<IntPtr, DesktopWindow>();
        foreach (var window in handles)
            if (IsAppWindow(window) && Describe(window, window, pinned) is { } app) found[window] = app;
        // Dialogs, palettes and shadows belong to an app window and have to come and go with it.
        foreach (var window in handles)
        {
            var owner = RootOwner(window);
            if (owner == window || !found.ContainsKey(owner)) continue;
            if (Describe(window, owner, pinned) is { } dialog) found[window] = dialog;
        }
        return found.Values.ToArray();
    }

    private static IntPtr RootOwner(IntPtr window)
    {
        // GetAncestor only follows owners of windows with the popup style, which many dialogs do not use.
        for (var depth = 0; depth < 16; depth++)
        {
            var owner = NativeMethods.GetWindow(window, NativeMethods.GW_OWNER);
            if (owner == IntPtr.Zero) break;
            window = owner;
        }
        return window;
    }

    private static bool IsAppWindow(IntPtr window)
    {
        var style = NativeMethods.GetWindowLong(window, NativeMethods.GWL_EXSTYLE);
        if ((style & NativeMethods.WS_EX_APPWINDOW) == 0
            && (style & (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE)) != 0) return false;
        if ((style & NativeMethods.WS_EX_TRANSPARENT) != 0) return false; // Click-through overlays.
        if (ShellClasses.Contains(NativeMethods.ClassName(window))) return false;
        var text = new StringBuilder(8);
        if (NativeMethods.GetWindowText(window, text, text.Capacity) == 0) return false;
        return NativeMethods.IsIconic(window)
            || NativeMethods.GetWindowRect(window, out var bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    private DesktopWindow? Describe(IntPtr window, IntPtr root, HashSet<string> pinned)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId) return null;
        var (start, name) = NativeMethods.ProcessInfo(processId);
        if (ShellProcesses.Contains(name) || pinned.Contains(name)) return null;
        // Like a pinned app, a popout player or call is left out of workspaces, so switching never hides it.
        if (_stickyPopouts() && Popouts.Is(window)) return null;
        var title = new StringBuilder(256);
        NativeMethods.GetWindowText(window, title, title.Capacity);
        var result = new DesktopWindow(new WindowIdentity(window, (int)processId, start, NativeMethods.ClassName(window)), root,
            title.ToString(), MonitorOf(window), name);
        return _only == null || _only(result) ? result : null;
    }

    internal static string MonitorOf(IntPtr window)
    {
        var info = new NativeMethods.MONITORINFOEX { Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(), Device = "" };
        return NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info) ? info.Device : "";
    }

    private static bool IsCloaked(IntPtr window)
        => NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    public IReadOnlyList<string> Monitors() => System.Windows.Forms.Screen.AllScreens
        .Where(screen => screen.Primary || !_primaryOnly()).OrderByDescending(screen => screen.Primary)
        .Select(screen => screen.DeviceName).ToArray();

    public bool Exists(WindowIdentity window)
    {
        var handle = (IntPtr)window.Handle;
        if (!NativeMethods.IsWindow(handle)) return false;
        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        return processId == (uint)window.ProcessId && NativeMethods.ProcessInfo(processId).Start == window.ProcessStart
            && NativeMethods.ClassName(handle) == window.ClassName;
    }

    public bool IsVisible(nint window) => NativeMethods.IsWindowVisible(window);
    // Keeps the size, minimized state and stacking order, and does not take focus.
    public void Show(nint window) => NativeMethods.ShowWindowAsync(window, NativeMethods.SW_SHOWNA);
    public void Hide(nint window) => NativeMethods.ShowWindowAsync(window, NativeMethods.SW_HIDE);
    public nint Foreground() => NativeMethods.GetForegroundWindow();
    public void Focus(nint window)
    {
        // Focusing a minimized window would leave it minimized but active.
        if (!NativeMethods.IsIconic(window)) NativeMethods.SetForegroundWindow(window);
    }
    public void FocusDesktop() => NativeMethods.SetForegroundWindow(NativeMethods.GetShellWindow());
}

/// <summary>
/// Picture-in-picture players, mini players and call windows: small windows kept above the others, or named as
/// picture-in-picture. They are watched while other windows are used, so they are never tiled or resized.
/// </summary>
internal static class Popouts
{
    private const int WsExTopmost = 0x8;
    private static readonly string[] Titles = { "picture in picture", "picture-in-picture" };

    public static bool Is(nint window)
    {
        if (NativeMethods.IsIconic(window) || !NativeMethods.GetWindowRect(window, out var rect)) return false;
        var title = new StringBuilder(128);
        NativeMethods.GetWindowText(window, title, title.Capacity);
        var topmost = (NativeMethods.GetWindowLong(window, NativeMethods.GWL_EXSTYLE) & WsExTopmost) != 0;
        var info = new NativeMethods.MONITORINFOEX { Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(), Device = "" };
        if (!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var work = System.Drawing.Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        return IsPopout(title.ToString(), topmost, System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom), work);
    }

    /// <summary>A large always-on-top window, such as Task Manager kept on top, is still an ordinary window.</summary>
    internal static bool IsPopout(string title, bool topmost, System.Drawing.Rectangle bounds, System.Drawing.Rectangle work)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;
        if (Titles.Any(name => title.Contains(name, StringComparison.OrdinalIgnoreCase))) return true;
        return topmost && work.Width > 0 && work.Height > 0 && (long)bounds.Width * bounds.Height * 2 <= (long)work.Width * work.Height;
    }
}

/// <summary>Reports window activity as it happens, on the thread that created it.</summary>
internal sealed class WindowEvents : IDisposable
{
    private readonly NativeMethods.WinEventProc _callback;
    private readonly List<IntPtr> _hooks = new();

    public const uint MoveStarted = 0xA, MoveEnded = 0xB;

    public WindowEvents(Action<uint, nint> changed)
    {
        _callback = (_, eventId, window, objectId, childId, _, _) =>
        {
            if (window == IntPtr.Zero || objectId != 0 || childId != 0) return;
            // Skip controls inside windows. A destroyed window no longer has an ancestor.
            var top = NativeMethods.GetAncestor(window, 2);
            if (top == window || top == IntPtr.Zero) changed(eventId, window);
        };
        // Focus; start and end of a move; minimize and restore; destroy, show and hide; title; virtual desktop changes.
        foreach (var (min, max) in new (uint, uint)[] { (0x3, 0x3), (MoveStarted, MoveEnded), (0x16, 0x17), (0x8001, 0x8003), (0x800C, 0x800C), (0x8017, 0x8018) })
            _hooks.Add(NativeMethods.SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0, 2 /* out of context, skip own process */));
    }

    public void Dispose()
    {
        foreach (var hook in _hooks.Where(hook => hook != IntPtr.Zero)) NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
        GC.KeepAlive(_callback);
    }
}
