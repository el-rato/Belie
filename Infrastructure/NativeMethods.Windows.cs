using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperProfiles.Infrastructure;

// Window management, docking and system readings used by the top bar and workspaces.
internal static partial class NativeMethods
{
    internal const int SW_HIDE = 0;
    internal const int SW_MAXIMIZE = 3;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_SHOWNA = 8;
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const int WS_EX_APPWINDOW = 0x40000;
    internal const uint GW_OWNER = 4;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal const int DWMWA_CLOAKED = 14;

    /// <summary>Unlike ShowWindow, returns at once even when the window's app has stopped responding.</summary>
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    /// <summary>Lets another process bring its window to the front. Windows only grants this to a process that was just clicked.</summary>
    [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(uint processId);
    internal const uint ASFW_ANY = uint.MaxValue;
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hWnd, uint command);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [StructLayout(LayoutKind.Sequential)] private struct ThreadThrottling { public uint Version, Control, State; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] private static extern bool SetThreadInformation(IntPtr thread, int kind, ref ThreadThrottling value, int size);

    /// <summary>Windows slows processes without a window in front down to save power, which shows as stutter in what they animate.</summary>
    internal static void KeepThreadFast()
    {
        var value = new ThreadThrottling { Version = 1, Control = 1 };
        try { SetThreadInformation(GetCurrentThread(), 3, ref value, Marshal.SizeOf<ThreadThrottling>()); }
        catch (EntryPointNotFoundException) { }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public int Reserved1, Reserved2, MaxWidth, MaxHeight, MaxX, MaxY, MinTrackWidth, MinTrackHeight, MaxTrackWidth, MaxTrackHeight;
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, nint wParam,
        ref MINMAXINFO info, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);
    [DllImport("dwmapi.dll")] internal static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out SIZE size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint Flags;
        public RECT Destination;
        public RECT Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr context, IntPtr bounds, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr context, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    internal delegate void WinEventProc(IntPtr hook, uint eventId, IntPtr hWnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback,
        uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [DllImport("kernel32.dll")] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] internal static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }

    [DllImport("kernel32.dll")] internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public int Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public RECT Bounds;
        public IntPtr Parameter;
    }

    [DllImport("shell32.dll")] internal static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    internal static (long Start, string Name) ProcessInfo(uint processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return (0, "");
        try
        {
            if (!GetProcessTimes(process, out var start, out _, out _, out _)) start = 0;
            var path = new StringBuilder(1024);
            var size = path.Capacity;
            return (start, QueryFullProcessImageName(process, 0, path, ref size) ? Path.GetFileNameWithoutExtension(path.ToString()) : "");
        }
        finally { CloseHandle(process); }
    }

    internal static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(256);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }
}
