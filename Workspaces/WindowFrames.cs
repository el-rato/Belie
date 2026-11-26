using System.Drawing;
using System.Runtime.InteropServices;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Workspaces;

/// <summary>Where windows and screens are, for drawing workspace animations.</summary>
internal interface IWindowFrames
{
    /// <summary>The visible edges, without Windows' invisible resize borders.</summary>
    Rectangle Frame(nint window);
    Rectangle WorkArea(string monitor);
}

internal sealed class NativeWindowFrames : IWindowFrames
{
    private const int WsMaximize = 0x1000000;

    public Rectangle Frame(nint window)
    {
        NativeMethods.GetWindowRect(window, out var rect);
        var outer = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (!NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window) || (NativeMethods.GetWindowLong(window, NativeMethods.GWL_STYLE) & WsMaximize) != 0
            || NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT bounds, Marshal.SizeOf<NativeMethods.RECT>()) != 0)
            return outer;
        var frame = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        // Anything far off is a window caught halfway through moving, not a border.
        return outer.Contains(frame) && frame.Left - outer.Left <= 32 && outer.Right - frame.Right <= 32 && outer.Bottom - frame.Bottom <= 32 ? frame : outer;
    }

    public Rectangle WorkArea(string monitor)
    {
        // Screen caches working areas, and docking the bar changes them.
        var result = Rectangle.Empty;
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(), Device = "" };
            if (!NativeMethods.GetMonitorInfo(handle, ref info) || info.Device != monitor) return true;
            result = Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
            return false;
        }, IntPtr.Zero);
        return result;
    }
}
