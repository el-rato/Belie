using System.Runtime.InteropServices;

namespace WallpaperProfiles.Infrastructure;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    // ============ Live wallpaper host (WorkerW technique) ============

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    internal const int GWL_STYLE = -16;
    internal const int WS_CHILD = 0x40000000;
    internal const int WS_POPUP = unchecked((int)0x80000000);
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;
    internal static readonly IntPtr HwndBottom = new IntPtr(1);
    internal const int SW_SHOW = 5;
    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;
    internal const int WM_DISPLAYCHANGE = 0x007E;

    /// <summary>
    /// Tells Progman to spawn a WorkerW window between the wallpaper and the desktop
    /// icons (message 0x052C). Children of that WorkerW render "behind the desktop icons".
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x20;
    internal const int WS_EX_TOOLWINDOW = 0x80;
    internal const int WS_EX_LAYERED = 0x80000;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const uint LWA_ALPHA = 2;

    [DllImport("user32.dll")]
    internal static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    internal static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }
        var milliseconds = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
