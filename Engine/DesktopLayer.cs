using System.Runtime.InteropServices;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Engine;

internal enum DesktopAttachMode
{
    /// <summary>Child of a top-level WorkerW behind the desktop icons (classic Windows).</summary>
    TopLevelWorkerW,
    /// <summary>Child of Progman's full-size wallpaper WorkerW (Windows 11 22H2/23H2 style).</summary>
    WallpaperWorkerWChild,
    /// <summary>
    /// Top-level window seated directly above Progman, click-through. Used on Windows builds
    /// where the desktop no longer spawns/relocates the wallpaper WorkerW (the wallpaper keeps
    /// being painted by the icons window, which hides anything below it). Trade-off: the live
    /// wallpaper covers the desktop icons visually while active (mouse clicks still pass
    /// through to the desktop).
    /// </summary>
    Overlay,
    Failed,
}

/// <summary>
/// Attaches a window to the desktop so it renders as a live wallpaper, using the best
/// technique the running Windows build supports. Pure Win32 — no WPF involvement.
/// </summary>
internal static class DesktopLayer
{
    private const uint WM_SPAWN_WORKERW = 0x052C;

    // The mode used for the current attach, so display-change repositioning can keep
    // the same geometry (overlay windows must never regain full-monitor size, or the
    // shell treats them as fullscreen and hides the taskbar).
    private static DesktopAttachMode _lastMode = DesktopAttachMode.Failed;

    /// <summary>Repositions after a display change, preserving the attach mode's geometry.</summary>
    public static void RepositionForDisplayChange(IntPtr hwnd)
    {
        if (_lastMode == DesktopAttachMode.Overlay)
        {
            var progman = NativeMethods.FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
            {
                progman = NativeMethods.GetShellWindow();
            }
            if (progman != IntPtr.Zero)
            {
                SeatOverlay(hwnd, progman);
            }
        }
        else
        {
            PositionOverVirtualScreen(hwnd);
        }
    }

    public static DesktopAttachMode Attach(IntPtr hwnd)
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            progman = NativeMethods.GetShellWindow();
        }
        if (progman == IntPtr.Zero)
        {
            Logger.Error("Desktop window (Progman) not found; cannot attach live wallpaper.");
            return DesktopAttachMode.Failed;
        }
        // Ask Progman to relocate wallpaper painting into a WorkerW below the icons (the
        // classic 0x052C trick). Build-dependent: several variants exist.
        IntPtr unusedResult;
        _ = NativeMethods.SendMessageTimeout(
            progman, WM_SPAWN_WORKERW, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG, 1000, out unusedResult);

        // Windows 11 24H2+ (build 26100+) stopped relocating wallpaper painting into the
        // WorkerW layer — windows inside it stay hidden beneath the wallpaper bitmap.
        // Those builds go straight to overlay mode; older builds keep the nicer layers
        // that leave the desktop icons visible.
        var legacyLayersSupported = Environment.OSVersion.Version.Build < 26100;

        // 1) Classic: a top-level WorkerW that paints the wallpaper behind the desktop icons.
        //    The spawn can complete asynchronously, so give it a moment.
        var parent = IntPtr.Zero;
        for (var attempt = 0; attempt < 5 && parent == IntPtr.Zero; attempt++)
        {
            parent = FindWorkerWBehindIcons();
            if (parent == IntPtr.Zero)
            {
                System.Threading.Thread.Sleep(60);
            }
        }
        if (parent != IntPtr.Zero)
        {
            if (MakeChild(hwnd, parent))
            {
                _ = NativeMethods.SetWindowPos(
                    hwnd, NativeMethods.HwndBottom, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                    | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                _lastMode = DesktopAttachMode.TopLevelWorkerW;
                Logger.Info("Live wallpaper attached to the classic WorkerW layer.");
                return DesktopAttachMode.TopLevelWorkerW;
            }
            return DesktopAttachMode.Failed;
        }

        if (!legacyLayersSupported)
        {
            return AttachOverlay(hwnd, progman);
        }

        // 2) Newer Windows builds keep the wallpaper WorkerW as a CHILD of Progman
        //    (sibling below SHELLDLL_DefView) and never spawn a top-level one.
        var childWorkerW = FindWallpaperWorkerWChild(progman);
        if (childWorkerW != IntPtr.Zero)
        {
            if (MakeChild(hwnd, childWorkerW))
            {
                _ = NativeMethods.SetWindowPos(
                    hwnd, NativeMethods.HwndBottom, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                    | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                _lastMode = DesktopAttachMode.WallpaperWorkerWChild;
                Logger.Info("Live wallpaper attached to Progman's wallpaper WorkerW layer.");
                return DesktopAttachMode.WallpaperWorkerWChild;
            }
            return DesktopAttachMode.Failed;
        }

        // 3) Overlay: top-level window directly above Progman, click-through.
        return AttachOverlay(hwnd, progman);
    }

    private static bool MakeChild(IntPtr hwnd, IntPtr parent)
    {
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        style = (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD;
        _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);
        if (NativeMethods.SetParent(hwnd, parent) == IntPtr.Zero)
        {
            Logger.Error($"SetParent failed (Win32 error {Marshal.GetLastWin32Error()}).");
            return false;
        }
        PositionOverVirtualScreen(hwnd);
        return true;
    }

    private static DesktopAttachMode AttachOverlay(IntPtr hwnd, IntPtr progman)
    {
        // Undo any child styling, become a borderless top-level again.
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        style = (style & ~NativeMethods.WS_CHILD) | NativeMethods.WS_POPUP;
        _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);
        _ = NativeMethods.SetParent(hwnd, IntPtr.Zero);

        // Click-through so the desktop stays usable, invisible in alt-tab/taskbar.
        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        _ = NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
        _ = NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);

        SeatOverlay(hwnd, progman);
        _lastMode = DesktopAttachMode.Overlay;
        Logger.Info("Live wallpaper running in overlay mode (this Windows build does not expose the wallpaper layer; desktop icons are covered while a live wallpaper is active).");
        return DesktopAttachMode.Overlay;
    }

    /// <summary>
    /// Seats the overlay directly above Progman, one pixel short of the full virtual
    /// screen — full-monitor coverage makes the shell treat the window as a fullscreen
    /// app and auto-hide the taskbar.
    /// </summary>
    private static void SeatOverlay(IntPtr hwnd, IntPtr progman)
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (w <= 0 || h <= 0)
        {
            return;
        }
        var ok = NativeMethods.SetWindowPos(
            hwnd, progman, x, y, w, h - 1,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        if (!ok)
        {
            Logger.Error("Seating the live wallpaper overlay above Progman failed.");
        }
    }

    /// <summary>
    /// True when some app window is covering (most of) the desktop, so live frames would
    /// be invisible. While covered the render loop should pause — it saves the CPU/GPU
    /// and memory bandwidth that otherwise starve the foreground app and make both the
    /// app and the video stutter. Returns false when the desktop itself has focus or the
    /// foreground window is small (wallpaper still partially visible).
    /// </summary>
    public static bool IsDesktopCovered()
    {
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero)
            {
                return false;
            }
            if (!NativeMethods.IsWindowVisible(fg) || NativeMethods.IsIconic(fg))
            {
                return false;
            }
            var cls = GetClassName(fg);
            // The desktop / shell itself having focus means the wallpaper is visible.
            switch (cls)
            {
                case "Progman":
                case "WorkerW":
                case "SHELLDLL_DefView":
                case "SysListView32":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "TaskListThumbnailWnd":
                case "Windows.UI.Core.CoreWindow": // Start menu / search overlay
                    return false;
            }
            if (!NativeMethods.GetWindowRect(fg, out var rect))
            {
                return false;
            }
            var fw = rect.Right - rect.Left;
            var fh = rect.Bottom - rect.Top;
            if (fw <= 0 || fh <= 0)
            {
                return false;
            }
            var vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            var vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            var vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            var vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (vw <= 0 || vh <= 0)
            {
                return false;
            }
            // Intersection of the foreground window with the virtual screen.
            var ix0 = Math.Max(rect.Left, vx);
            var iy0 = Math.Max(rect.Top, vy);
            var ix1 = Math.Min(rect.Right, vx + vw);
            var iy1 = Math.Min(rect.Bottom, vy + vh);
            var iw = ix1 - ix0;
            var ih = iy1 - iy0;
            if (iw <= 0 || ih <= 0)
            {
                return false;
            }
            var cover = (double)iw * ih / ((double)vw * vh);
            // A maximized window still leaves the taskbar visible (~5-8% of the screen),
            // so 80% coverage reliably means "the wallpaper can't be seen".
            return cover >= 0.8;
        }
        catch
        {
            return false;
        }
    }

    private static string GetClassName(IntPtr hwnd)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            return NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }
        catch
        {
            return "";
        }
    }

    public static void PositionOverVirtualScreen(IntPtr hwnd)
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (w <= 0 || h <= 0)
        {
            return;
        }
        _ = NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, x, y, w, h,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>The full-size WorkerW that lives as a CHILD of Progman (Windows 11 22H2+).</summary>
    private static IntPtr FindWallpaperWorkerWChild(IntPtr progman)
    {
        var child = IntPtr.Zero;
        while (true)
        {
            child = NativeMethods.FindWindowEx(progman, child, "WorkerW", null);
            if (child == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            if (NativeMethods.GetWindowRect(child, out var rect)
                && rect.Right - rect.Left >= 640 && rect.Bottom - rect.Top >= 480)
            {
                return child;
            }
        }
    }

    private static IntPtr FindWorkerWBehindIcons()
    {
        IntPtr workerw = IntPtr.Zero;
        _ = NativeMethods.EnumWindows((top, _) =>
        {
            var defview = NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defview != IntPtr.Zero)
            {
                workerw = NativeMethods.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            }
            return workerw == IntPtr.Zero;
        }, IntPtr.Zero);
        return workerw;
    }
}
