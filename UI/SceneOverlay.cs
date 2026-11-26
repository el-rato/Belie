using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Workspaces;

namespace WallpaperProfiles.UI;

/// <summary>
/// Animates other apps' windows smoothly. It covers one monitor's work area with live pictures of the windows
/// that the desktop compositor draws, moves those, and rearranges the real windows once underneath. Moving the
/// real windows every frame would make each app repaint and stutter.
/// </summary>
internal sealed class SceneOverlay : IDisposable
{
    // Kept up this long after the motion so windows can finish hiding and repainting out of sight.
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(120);

    /// <summary>A plain window: WPF's own would remove the style that lets clicks through.</summary>
    private sealed class Canvas : System.Windows.Forms.NativeWindow
    {
        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg == 0x14 /* WM_ERASEBKGND */)
            {
                using var graphics = Graphics.FromHdc(message.WParam);
                graphics.Clear(Color.Black);
                message.Result = 1;
                return;
            }
            base.WndProc(ref message);
        }
    }

    private sealed class Picture
    {
        public nint Window, Thumbnail;
        public Rectangle From, To;
        public double FromScale = 1, FromOpacity = 1, ToOpacity = 1;
        public Func<double, double> Ease = Motion.Slide;
        public double Time;
        public bool Animated;
        public Rectangle Border; // Window size beyond the visible frame, when the picture includes it.
        public Rectangle Last;
        public byte LastOpacity;
    }

    private readonly IWindowFrames _tiles;
    private readonly Func<double> _speed;
    private readonly Func<WorkspaceMotion> _motion;
    private Canvas? _source;
    private readonly List<Picture> _pictures = new();
    private readonly HashSet<nint> _quiet = new();
    private Rectangle _area;
    private Action? _finish;
    private int _run;
    private bool _disposed;

    public bool IsRunning => _pictures.Count > 0;
    public event Action? Completed;
    internal int FrameCount { get; private set; }

    public SceneOverlay(IWindowFrames tiles, Func<double> speed, Func<WorkspaceMotion>? motion = null)
    { _tiles = tiles; _speed = speed; _motion = motion ?? (() => WorkspaceMotion.Horizontal); }

    /// <summary>Moves or fades one workspace out while the next comes in.</summary>
    public void Slide(string monitor, int direction, IReadOnlyCollection<nint> leaving, IReadOnlyCollection<nint> arriving, Action show, Action hide)
    {
        var area = _tiles.WorkArea(monitor);
        var motion = _motion();
        var distance = direction * (motion == WorkspaceMotion.Vertical ? area.Height : area.Width);
        Run(area, show, hide, window =>
        {
            var leaves = leaving.Contains(window);
            if (!leaves && !arriving.Contains(window)) return null;
            var frame = _tiles.Frame(window);
            if (motion == WorkspaceMotion.Fade) return new Picture { From = frame, To = frame, FromOpacity = leaves ? 1 : 0, ToOpacity = leaves ? 0 : 1,
                FromScale = leaves ? 1 : .97, Ease = Motion.Slide, Time = Motion.MoveTime, Animated = true };
            var offset = leaves ? -distance : distance;
            var away = motion == WorkspaceMotion.Vertical ? frame with { Y = frame.Y + offset } : frame with { X = frame.X + offset };
            return new Picture { From = leaves ? frame : away, To = leaves ? away : frame, Ease = Motion.Slide, Time = Motion.SlideTime, Animated = true };
        }, arriving, leaving.Concat(arriving));
    }

    private async void Run(Rectangle area, Action begin, Action end, Func<nint, Picture?> animate, IReadOnlyCollection<nint> arriving, IEnumerable<nint> changing)
    {
        Finish(keepVisible: true);
        Quiet(changing);
        var run = ++_run;
        var began = false;
        _finish = () => { if (!began) { began = true; begin(); } end(); };
        try
        {
            if (_disposed || area.Width < 1 || area.Height < 1) return;
            Cover(area);
            foreach (var window in Stack(area, _source!.Handle)) Add(window, animate(window));
            Draw(0);
            Reveal();
            await Frame(); // The pictures must be on screen before the windows below them change.
            if (run != _run) return;
            began = true; begin();
            if (arriving.Count > 0)
            {
                // A hidden window has no picture until it has been shown and drawn.
                for (var i = 0; i < 2; i++) await Frame();
                if (run != _run) return;
                foreach (var window in arriving) Add(window, animate(window));
                Draw(0);
                await Frame();
            }
            var speed = Math.Clamp(_speed(), .25, 4);
            var total = _pictures.Where(picture => picture.Animated).Select(picture => picture.Time).DefaultIfEmpty(0).Max();
            FrameCount = 0;
            // Off this thread, so nothing else Belie does can make the motion stutter.
            await OnOwnThread(() =>
            {
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed.TotalMilliseconds * speed < total)
                {
                    lock (_pictures)
                    {
                        if (run != _run) return;
                        Draw(clock.Elapsed.TotalMilliseconds * speed);
                        FrameCount++;
                    }
                    WaitForFrame();
                }
            });
            if (run != _run) return;
            Draw(total);
            var finish = _finish; _finish = null;
            finish?.Invoke();
            await Task.Delay(Hold);
        }
        catch (Exception ex) { Logger.Error("A workspace animation failed.", ex); }
        finally { if (run == _run) { Finish(keepVisible: false); if (!_disposed) Completed?.Invoke(); } }
    }

    /// <summary>Ends any animation now, with the windows in their final places.</summary>
    public void Finish(bool keepVisible = false)
    {
        lock (_pictures) _run++;
        var finish = _finish; _finish = null;
        try { finish?.Invoke(); }
        catch (Exception ex) { Logger.Error("Finishing a workspace animation failed.", ex); }
        if (!keepVisible) { Hide(); Unquiet(); }
        lock (_pictures)
        {
            foreach (var picture in _pictures) NativeMethods.DwmUnregisterThumbnail(picture.Thumbnail);
            _pictures.Clear();
        }
    }

    /// <summary>Windows fades windows in and out itself, which would still be playing after the slide has ended.</summary>
    private void Quiet(IEnumerable<nint> windows)
    {
        var off = 1;
        foreach (var window in windows) if (_quiet.Add(window)) NativeMethods.DwmSetWindowAttribute(window, 3, ref off, sizeof(int));
    }

    private void Unquiet()
    {
        var off = 0;
        foreach (var window in _quiet) NativeMethods.DwmSetWindowAttribute(window, 3, ref off, sizeof(int));
        _quiet.Clear();
    }

    /// <summary>Runs ahead of the apps that are busy redrawing themselves at their new sizes.</summary>
    private static Task OnOwnThread(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            NativeMethods.KeepThreadFast();
            try { action(); done.SetResult(); } catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Workspace animation" }.Start();
        return done.Task;
    }

    private static Task Frame() => Task.Run(WaitForFrame);

    private static void WaitForFrame()
    {
        // Waits for the compositor, so motion matches the display's refresh rate.
        if (NativeMethods.DwmFlush() != 0) Thread.Sleep(16);
    }

    private void Cover(Rectangle area)
    {
        _area = area;
        if (_source == null)
        {
            _source = new Canvas();
            _source.CreateHandle(new System.Windows.Forms.CreateParams
            {
                Caption = "Belie workspace animation", Style = NativeMethods.WS_POPUP,
                // Layered and transparent together let clicks reach the windows below.
                ExStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT
                    | NativeMethods.WS_EX_LAYERED | 8 /* topmost */,
                X = area.X, Y = area.Y, Width = area.Width, Height = area.Height
            });
            var off = 1; // Otherwise Windows fades the overlay itself in and out.
            NativeMethods.DwmSetWindowAttribute(_source.Handle, 3, ref off, sizeof(int));
            NativeMethods.SetLayeredWindowAttributes(_source.Handle, 0, 255, 2);
        }
        NativeMethods.SetWindowPos(_source.Handle, IntPtr.Zero, area.X, area.Y, area.Width, area.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    private void Reveal() => NativeMethods.SetWindowPos(_source!.Handle, new IntPtr(-1), 0, 0, 0, 0,
        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

    private void Hide() { if (_source != null) NativeMethods.ShowWindow(_source.Handle, NativeMethods.SW_HIDE); }

    /// <summary>Everything showing in the area, from the wallpaper up.</summary>
    private static List<nint> Stack(Rectangle area, nint overlay)
    {
        var windows = new List<nint>();
        NativeMethods.EnumWindows((window, _) =>
        {
            if (window == overlay || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window)) return true;
            if (NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (!NativeMethods.GetWindowRect(window, out var rect) || !Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom).IntersectsWith(area)) return true;
            windows.Add(window); // Belie's own widgets included.
            return true;
        }, IntPtr.Zero);
        windows.Reverse();
        return windows;
    }

    private void Add(nint window, Picture? picture)
    {
        if (_pictures.Any(existing => existing.Window == window)) return;
        if (NativeMethods.DwmRegisterThumbnail(_source!.Handle, window, out var thumbnail) != 0) return;
        NativeMethods.GetWindowRect(window, out var rect);
        var outer = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var frame = _tiles.Frame(window);
        picture ??= new Picture { From = frame, To = frame };
        picture.Window = window; picture.Thumbnail = thumbnail;
        // Pictures of some windows include the invisible border and others do not; the size tells which.
        if (NativeMethods.DwmQueryThumbnailSourceSize(thumbnail, out var size) == 0
            && Math.Abs(size.Width - outer.Width) < Math.Abs(size.Width - frame.Width))
            picture.Border = Rectangle.FromLTRB(frame.Left - outer.Left, frame.Top - outer.Top, outer.Right - frame.Right, outer.Bottom - frame.Bottom);
        _pictures.Add(picture);
    }

    private void Draw(double time)
    {
        foreach (var picture in _pictures)
        {
            var progress = picture.Time <= 0 ? 1 : Math.Clamp(time / picture.Time, 0, 1);
            var eased = picture.Ease(progress);
            var bounds = Blend(picture.From, picture.To, eased);
            var scale = picture.FromScale + (1 - picture.FromScale) * eased;
            if (scale != 1) bounds.Inflate((int)Math.Round(bounds.Width * (scale - 1) / 2), (int)Math.Round(bounds.Height * (scale - 1) / 2));
            bounds = Rectangle.FromLTRB(bounds.Left - picture.Border.Left, bounds.Top - picture.Border.Top,
                bounds.Right + picture.Border.Right, bounds.Bottom + picture.Border.Bottom);
            var opacity = (byte)Math.Round(255 * Math.Clamp(picture.FromOpacity + (picture.ToOpacity - picture.FromOpacity) * Motion.Fade(Math.Clamp(time / Motion.FadeTime, 0, 1)), 0, 1));
            if (bounds == picture.Last && opacity == picture.LastOpacity) continue;
            picture.Last = bounds; picture.LastOpacity = opacity;
            var properties = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                Flags = 0x1 | 0x4 | 0x8, // Destination, opacity, visibility.
                Destination = new NativeMethods.RECT
                {
                    Left = bounds.Left - _area.Left, Top = bounds.Top - _area.Top, Right = bounds.Right - _area.Left, Bottom = bounds.Bottom - _area.Top
                },
                Opacity = opacity, Visible = true
            };
            NativeMethods.DwmUpdateThumbnailProperties(picture.Thumbnail, ref properties);
        }
    }

    internal static Rectangle Blend(Rectangle from, Rectangle to, double amount) => new(
        (int)Math.Round(from.X + (to.X - from.X) * amount), (int)Math.Round(from.Y + (to.Y - from.Y) * amount),
        Math.Max(1, (int)Math.Round(from.Width + (to.Width - from.Width) * amount)),
        Math.Max(1, (int)Math.Round(from.Height + (to.Height - from.Height) * amount)));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Finish();
        _source?.DestroyHandle();
        _source = null;
    }
}
