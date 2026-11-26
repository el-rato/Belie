using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Workspaces;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace WallpaperProfiles.UI;

/// <summary>
/// The team's leader, walking along the top edge of the window in front, or on the taskbar when there is none.
/// Clicks go through it, and it never takes the focus.
/// </summary>
internal sealed class DesktopWalker : Window
{
    private const double Side = 48, Speed = 1.4, Gravity = 1.1;
    private static DesktopWalker? _shown;
    private static readonly HashSet<string> Floors = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private readonly BitmapSource[] _frames;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly ScaleTransform _facing = new(1, 1);
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly NativeWindowFrames _windows = new();
    private readonly Random _random = new();
    private nint _handle;
    private double _x = double.NaN, _y, _fall;
    private int _direction = 1, _rest, _tick;

    /// <summary>Shows, changes or hides the walker to match the team.</summary>
    public static void Apply(TeamState state)
    {
        var creatures = CreaturePack.Installed();
        var leader = state.Members.ElementAtOrDefault(state.Leader) is { } member ? creatures.FirstOrDefault(creature => creature.Id == member.Id) : null;
        if (!state.Walks || leader == null) { _shown?.Close(); _shown = null; return; }
        if (_shown?.Tag as string == leader.Id) return;
        try
        {
            var frames = leader.Frames.Select(frame =>
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(CreaturePack.FramePath(frame)); image.EndInit();
                image.Freeze();
                return (BitmapSource)image;
            }).ToArray();
            var next = new DesktopWalker(frames) { Tag = leader.Id };
            _shown?.Close();
            _shown = next;
            next.Show();
        }
        catch (Exception ex) { Logger.Warn("The walking creature could not be shown: " + ex.Message); }
    }

    private DesktopWalker(BitmapSource[] frames)
    {
        _frames = frames;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; Focusable = false;
        Width = Height = Side; Left = -10000; Top = -10000;
        Title = "Belie creature";
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _image.RenderTransformOrigin = new Point(.5, .5);
        _image.RenderTransform = _facing;
        _image.Source = frames[0];
        Content = _image;
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLong(_handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        };
        _timer.Tick += (_, _) => Step();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Where to stand: the top edge of the window in front, or the bottom of the screen's work area.</summary>
    private System.Drawing.Rectangle Ground()
    {
        var front = NativeMethods.GetForegroundWindow();
        if (front != 0 && front != _handle && NativeMethods.IsWindowVisible(front) && !NativeMethods.IsIconic(front)
            && !Floors.Contains(NativeMethods.ClassName(front)))
        {
            var frame = _windows.Frame(front);
            var screen = System.Windows.Forms.Screen.FromRectangle(frame);
            // A maximized or full-screen window's top edge is under the bar or off the screen.
            if (frame.Width > 160 && frame.Top > screen.WorkingArea.Top + 4 && frame.Top < screen.Bounds.Bottom - 80) return frame;
        }
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        return new System.Drawing.Rectangle(work.Left, work.Bottom, work.Width, 0);
    }

    private void Step()
    {
        if (_handle == 0) return;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = (int)Math.Round(Side * scale);
        var ground = Ground();
        double left = ground.Left + size / 2.0, right = ground.Right - size / 2.0;
        if (double.IsNaN(_x)) { _x = (left + right) / 2; _y = ground.Top; }
        _tick++;

        var airborne = _fall != 0 || _y < ground.Top - .5;
        if (!airborne && _y > ground.Top + 2)
        {
            // The new ground is higher: hop up onto it.
            _fall = -Math.Sqrt(2 * Gravity * (_y - ground.Top + 24 * scale));
            airborne = true;
        }
        if (airborne)
        {
            _fall += Gravity * scale;
            _y += _fall;
            _x = Math.Clamp(_x + _direction * Speed * scale, left, Math.Max(left, right));
            if (_fall > 0 && _y >= ground.Top) { _y = ground.Top; _fall = 0; }
        }
        else if (_rest > 0) _rest--;
        else
        {
            _x += _direction * Speed * scale;
            if (_x <= left || _x >= right) { _x = Math.Clamp(_x, left, Math.Max(left, right)); _direction = -_direction; }
            else if (_random.NextDouble() < .006) { _rest = 30 + _random.Next(90); if (_random.NextDouble() < .5) _direction = -_direction; }
        }
        _facing.ScaleX = _direction;
        _image.Source = _frames[_rest > 0 && !airborne ? 0 : _tick / 6 % _frames.Length];
        NativeMethods.SetWindowPos(_handle, new IntPtr(-1), (int)Math.Round(_x - size / 2.0), (int)Math.Round(_y - size), 0, 0, 0x0001 | 0x0010);
    }
}
