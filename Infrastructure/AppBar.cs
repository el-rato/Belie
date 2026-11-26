using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WallpaperProfiles.Infrastructure;

/// <summary>Reserves a strip along the top or bottom of a monitor, the way the taskbar does, so maximized windows stay clear of it.</summary>
internal sealed class AppBar : IDisposable
{
    private const uint New = 0, Remove = 1, QueryPos = 2, SetPos = 3, Activate = 6, WindowPosChanged = 9;
    private const uint EdgeTop = 1, EdgeBottom = 3;
    private const int NotifyPosChanged = 1, NotifyFullscreen = 2;
    private const int WmDestroy = 0x0002, WmActivate = 0x0006, WmWindowPosChanged = 0x0047;
    private static readonly uint Callback = NativeMethods.RegisterWindowMessage("Belie.TopBar.AppBar");
    private static readonly uint TaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private readonly HwndSource _source;
    private readonly IntPtr _window;
    private System.Drawing.Rectangle _monitor;
    private int _height;
    private bool _bottom;
    private bool _docking;
    private int _overhang;
    private bool _reserve = true, _reserved;
    private bool _disposed;

    /// <summary>Raised when an app on this desktop enters (true) or leaves (false) full screen.</summary>
    public event Action<bool>? FullscreenChanged;
    public System.Drawing.Rectangle Bounds { get; private set; }

    public AppBar(HwndSource source)
    {
        _source = source;
        _window = source.Handle; // The source forgets it while closing, when it is still needed to release the space.
        _source.AddHook(Receive);
        Register();
    }

    private NativeMethods.APPBARDATA Data() => new()
    {
        Size = Marshal.SizeOf<NativeMethods.APPBARDATA>(), Window = _window, CallbackMessage = Callback, Edge = _bottom ? EdgeBottom : EdgeTop
    };

    private void Register()
    {
        var data = Data();
        NativeMethods.SHAppBarMessage(New, ref data);
    }

    /// <param name="overhang">How far the window reaches past the space it reserves, to draw over other windows.</param>
    /// <param name="reserve">False to draw over windows instead of keeping them clear, for a bar that hides itself.</param>
    public void Dock(System.Drawing.Rectangle monitor, int height, bool bottom = false, int overhang = 0, bool reserve = true)
    {
        _overhang = overhang;
        if (!reserve && _reserved)
        {
            // Registering again gives the space back while still hearing about full-screen apps.
            var data = Data();
            NativeMethods.SHAppBarMessage(Remove, ref data);
            _reserved = false;
            Register();
        }
        _reserve = reserve;
        if (bottom != _bottom)
        {
            // Windows keeps the old edge's space unless the bar registers again.
            var data = Data();
            NativeMethods.SHAppBarMessage(Remove, ref data);
            _bottom = bottom;
            Register();
        }
        _monitor = monitor; _height = height;
        Dock();
    }

    private void Dock()
    {
        if (_disposed || _docking || _height <= 0) return;
        _docking = true;
        try
        {
            if (!_reserve)
            {
                Bounds = _bottom ? System.Drawing.Rectangle.FromLTRB(_monitor.Left, _monitor.Bottom - _height, _monitor.Right, _monitor.Bottom)
                    : System.Drawing.Rectangle.FromLTRB(_monitor.Left, _monitor.Top, _monitor.Right, _monitor.Top + _height);
                NativeMethods.SetWindowPos(_window, IntPtr.Zero, Bounds.X, _bottom ? Bounds.Y - _overhang : Bounds.Y, Bounds.Width, Bounds.Height + _overhang,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                return;
            }
            var data = Data();
            data.Bounds = _bottom
                ? new NativeMethods.RECT { Left = _monitor.Left, Top = _monitor.Bottom - _height, Right = _monitor.Right, Bottom = _monitor.Bottom }
                : new NativeMethods.RECT { Left = _monitor.Left, Top = _monitor.Top, Right = _monitor.Right, Bottom = _monitor.Top + _height };
            // Windows moves the edge when a taskbar or another bar is already there.
            NativeMethods.SHAppBarMessage(QueryPos, ref data);
            if (_bottom) data.Bounds.Top = data.Bounds.Bottom - _height;
            else data.Bounds.Bottom = data.Bounds.Top + _height;
            NativeMethods.SHAppBarMessage(SetPos, ref data);
            _reserved = true;
            Bounds = System.Drawing.Rectangle.FromLTRB(data.Bounds.Left, data.Bounds.Top, data.Bounds.Right, data.Bounds.Bottom);
            NativeMethods.SetWindowPos(_window, IntPtr.Zero, Bounds.X, _bottom ? Bounds.Y - _overhang : Bounds.Y, Bounds.Width, Bounds.Height + _overhang,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }
        finally { _docking = false; }
    }

    private IntPtr Receive(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        if (message == Callback)
        {
            if (wParam == NotifyPosChanged) Dock();
            else if (wParam == NotifyFullscreen) FullscreenChanged?.Invoke(lParam != IntPtr.Zero);
            handled = true;
        }
        else if (message == WmDestroy) Dispose();
        else if (message == TaskbarCreated) { Register(); Dock(); } // Explorer restarted and forgot every bar.
        else if (message is WmActivate or WmWindowPosChanged && !_docking)
        {
            var data = Data();
            NativeMethods.SHAppBarMessage(message == WmActivate ? Activate : WindowPosChanged, ref data);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = Data();
        NativeMethods.SHAppBarMessage(Remove, ref data);
        _source.RemoveHook(Receive);
    }
}
