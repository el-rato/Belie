using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Workspaces;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace WallpaperProfiles.UI;

/// <summary>Where the walker is this frame: a ground (or, for bats, a ceiling) from left to right, and how big to draw it.</summary>
/// <param name="Fit">1 for full size; smaller to fit inside the bar.</param>
/// <param name="OnBar">Walking inside the bar, where there is no room to hop or float high.</param>
internal readonly record struct Footing(double Left, double Right, double Y, bool Ceiling, double Fit = 1, bool OnBar = false);

/// <summary>
/// The team's leader, out on the desktop: on top of the window in front, otherwise along Belie's bar, and on the
/// taskbar only when there is no bar. Bats hang upside down under the bar. Clicks go through it, it never takes the
/// focus, and it keeps out of the way of full-screen apps and games.
/// </summary>
internal sealed class DesktopWalker : Window
{
    /// <summary>The sprite's size, room around it for squash and tilt, and sky above it for floating and flying.</summary>
    private const double Side = 48, Room = 14, Sky = 64;
    private const string BarTitle = "Belie top bar";
    private static DesktopWalker? _shown;
    private static readonly HashSet<string> Floors = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private readonly BitmapSource[] _frames;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Width = Side, Height = Side,
        VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
    private readonly Grid _root = new();
    private readonly ScaleTransform _fitScale = new(1, 1);
    private readonly ScaleTransform _flip = new(1, 1, Side / 2, Side / 2);
    private readonly ScaleTransform _shape = new(1, 1);
    private readonly RotateTransform _tilt = new();
    private readonly TranslateTransform _lift = new();
    private readonly NativeWindowFrames _windows = new();
    private readonly WalkerMotion _motion;
    private readonly CreatureEffects? _effects;
    private readonly CreatureEffectsView? _effectsView;
    private readonly (double X, double Y) _mouth, _tail;
    private readonly Stopwatch _clock = new();
    private nint _handle;
    private double _last, _fit = 1, _barCheckedAt = -10;
    private int _shownFrame = -1;
    private bool? _hanging;
    private nint _bar;
    private TopBarSettings? _barSettings;
    /// <summary>While hidden it stops following the screen's refresh and only looks in now and then.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _peek = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>Shows, changes or hides the walker to match the team.</summary>
    public static void Apply(TeamState state)
    {
        var creatures = CreaturePack.Installed();
        var member = state.Members.ElementAtOrDefault(state.Leader);
        var leader = member != null ? creatures.FirstOrDefault(creature => creature.Id == member.Id) : null;
        if (!state.Walks || leader == null || member == null) { _shown?.Close(); _shown = null; return; }
        var level = TeamProgress.Level(member.Xp);
        // A new level makes its effects bigger, so it comes back with a window that has room for them.
        var key = leader.Id + ":" + level;
        if (_shown?.Tag as string == key) return;
        try
        {
            var frames = CreatureSprite.Frames(leader);
            if (frames.Length == 0) throw new InvalidOperationException("its frames could not be read");
            var next = new DesktopWalker(frames, leader, CreatureEffects.StrengthFor(level)) { Tag = key };
            _shown?.Close();
            _shown = next;
            next.Show();
        }
        catch (Exception ex) { Logger.Warn("The walking creature could not be shown: " + ex.Message); }
    }

    private DesktopWalker(BitmapSource[] frames, Creature creature, double strength)
    {
        _frames = frames;
        _motion = new WalkerMotion(frames.Length, null, CreatureStyles.For(creature));
        // Dragons breathe fire, electric ones throw lightning, ghosts throw shadow balls, ground types kick up sand,
        // and fire types' tails burn. All of it grows with the creature's level.
        var power = CreatureEffects.PowerOf(creature.Types);
        var tailFlame = CreatureEffects.HasTailFlame(creature.Types);
        var reach = 0.0;
        if (power != CreaturePower.None || tailFlame)
        {
            _effects = new CreatureEffects(power, tailFlame, null, strength);
            _effectsView = new CreatureEffectsView(_effects);
            _mouth = creature.Mouth ?? CreatureEffects.DefaultMouth;
            _tail = creature.Tail ?? (tailFlame ? CreatureEffects.FindFlame(frames[0]) : null) ?? (power == CreaturePower.Shadow ? (.5, .55) : CreatureEffects.DefaultTail);
            reach = CreatureEffects.ReachFor(strength);
        }
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; Focusable = false;
        // Room above and beside the sprite for its bob, stretch and squash, and to each side for its effects.
        Width = Side + Room + 2 * reach; Height = Side + Room + Sky; Left = -10000; Top = -10000;
        Title = "Belie creature";
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _image.RenderTransform = new TransformGroup { Children = { _flip, _shape, _tilt, _lift } };
        _image.Source = frames[0];
        _root.Children.Add(_image);
        if (_effectsView != null) _root.Children.Add(_effectsView);
        _root.RenderTransform = _fitScale;
        Content = _root;
        SetHanging(false);
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLong(_handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        };
        // Moves with the screen's own refresh, so the walk is smooth instead of stepping on a timer.
        Loaded += (_, _) => { _clock.Start(); CompositionTarget.Rendering += OnFrame; };
        _peek.Tick += (_, _) =>
        {
            if (Where(VisualTreeHelper.GetDpi(this).DpiScaleX) == null) return;
            _peek.Stop(); Visibility = Visibility.Visible;
            _last = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnFrame;
        };
        Closed += (_, _) => { _peek.Stop(); CompositionTarget.Rendering -= OnFrame; };
    }

    /// <summary>Standing on its feet, or hanging from them upside down: the sprite flips, and swings from the top.</summary>
    private void SetHanging(bool hanging)
    {
        if (_hanging == hanging) return;
        _hanging = hanging;
        _image.VerticalAlignment = hanging ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _flip.ScaleY = hanging ? -1 : 1;
        var feet = hanging ? 0 : Side;
        _shape.CenterX = _tilt.CenterX = Side / 2;
        _shape.CenterY = _tilt.CenterY = feet;
        _fitScale.CenterX = Width / 2;
        _fitScale.CenterY = hanging ? 0 : Height;
    }

    /// <summary>True when a window fills its whole screen, as games and full-screen video do.</summary>
    internal static bool CoversScreen(System.Drawing.Rectangle frame, System.Drawing.Rectangle screen)
        => frame.Left <= screen.Left && frame.Top <= screen.Top && frame.Right >= screen.Right && frame.Bottom >= screen.Bottom;

    /// <summary>The bar itself inside its window, which also has room for popups; null when it is too small to walk on.</summary>
    internal static System.Drawing.Rectangle? BarStrip(System.Drawing.Rectangle window, TopBarSettings settings, double scale)
    {
        var margin = settings.BarMargin * scale;
        var height = settings.Height * scale;
        var left = window.Left + margin; var right = window.Right - margin;
        var top = settings.Edge == TopBarEdge.Bottom ? window.Bottom - margin - height : window.Top + margin;
        return right - left < 100 ? null : new System.Drawing.Rectangle((int)Math.Round(left), (int)Math.Round(top), (int)Math.Round(right - left), (int)Math.Round(height));
    }

    /// <summary>The bar's strip on screen and whether it sits at the bottom; null when there is none, or it has slid away.</summary>
    private (System.Drawing.Rectangle Strip, bool Bottom)? Bar(double scale, nint front, System.Drawing.Rectangle? frontFrame)
    {
        // The bar runs in its own process; find it, and read its settings, once a second.
        var now = _clock.Elapsed.TotalSeconds;
        if (now - _barCheckedAt > 1)
        {
            _barCheckedAt = now;
            _bar = NativeMethods.FindWindow(null!, BarTitle);
            try { _barSettings = new TopBarStore(TopBarProcess.DefaultFile).Load(); }
            catch { _barSettings = null; }
        }
        if (_bar == 0 || _barSettings is not { Enabled: true } settings || !NativeMethods.IsWindowVisible(_bar) || !NativeMethods.GetWindowRect(_bar, out var rect)) return null;
        if (BarStrip(System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom), settings, scale) is not { } strip) return null;
        // A bar that hides itself steps aside for any window that reaches it.
        if (settings.AutoHide && front != _bar && frontFrame is { } frame && frame.IntersectsWith(strip)) return null;
        return (strip, settings.Edge == TopBarEdge.Bottom);
    }

    /// <summary>Where to be this frame. Null hides it.</summary>
    private Footing? Where(double scale)
    {
        var front = NativeMethods.GetForegroundWindow();
        System.Drawing.Rectangle? frontFrame = null;
        if (front != 0 && front != _handle && NativeMethods.IsWindowVisible(front) && !NativeMethods.IsIconic(front)
            && !Floors.Contains(NativeMethods.ClassName(front)))
        {
            var frame = _windows.Frame(front);
            var screen = System.Windows.Forms.Screen.FromRectangle(frame);
            if (CoversScreen(frame, screen.Bounds)) return null; // A game or a film: stay out of the way.
            frontFrame = frame;
        }
        var bar = Bar(scale, front, frontFrame);
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;

        // Bats hang from the bar, or from the top of the screen while it is away.
        if (_motion.Style == MotionStyle.Hang)
            return bar is { Bottom: false } top ? new Footing(top.Strip.Left, top.Strip.Right, top.Strip.Bottom, true) : new Footing(work.Left, work.Right, work.Top, true);

        // On top of the window in front, when its top edge is in view (a maximized one's is not).
        if (frontFrame is { } window && window.Width > 160)
        {
            var screen = System.Windows.Forms.Screen.FromRectangle(window);
            var under = bar is { Bottom: false } above ? above.Strip.Bottom : screen.WorkingArea.Top;
            if (window.Top > under + 4 && window.Top < screen.Bounds.Bottom - 80) return new Footing(window.Left, window.Right, window.Top, false);
        }
        if (bar is { } found)
        {
            // A bar at the bottom is a floor to stand on; one at the top is walked along inside, made to fit.
            if (found.Bottom) return new Footing(found.Strip.Left, found.Strip.Right, found.Strip.Top, false);
            var fit = Math.Clamp((found.Strip.Height / scale - 6) / Side, .4, 1);
            return new Footing(found.Strip.Left + 8 * scale, found.Strip.Right - 8 * scale, found.Strip.Bottom - 3 * scale, false, fit, true);
        }
        return new Footing(work.Left, work.Right, work.Bottom, false);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_handle == 0) return;
        var now = _clock.Elapsed.TotalSeconds;
        if (now - _last < 1 / 75.0) return; // Smooth is enough; faster screens need not redraw it more.
        var seconds = now - _last; _last = now;

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (Where(scale) is not { } footing)
        {
            Visibility = Visibility.Hidden;
            _effects?.Clear();
            CompositionTarget.Rendering -= OnFrame; _peek.Start();
            return;
        }

        // Grows or shrinks smoothly when it moves between the bar and a window.
        _fit += (footing.Fit - _fit) * Math.Min(1, seconds * 6);
        if (Math.Abs(_fit - footing.Fit) < .01) _fit = footing.Fit;
        _fitScale.ScaleX = _fitScale.ScaleY = _fit;
        var half = Side * _fit * scale / 2;
        var pose = _motion.Advance(seconds, footing.Left + half, footing.Right - half, footing.Y, scale);
        SetHanging(pose.Hanging);

        if (pose.Frame != _shownFrame) { _shownFrame = pose.Frame; _image.Source = _frames[pose.Frame % _frames.Length]; }
        _shape.ScaleX = pose.ScaleX; _shape.ScaleY = pose.ScaleY;
        _tilt.Angle = pose.Angle;
        // Inside the bar there is only room for a small bob.
        var lift = Math.Min(pose.Lift, footing.OnBar ? 3 / _fit : Sky);
        _lift.Y = (pose.Hanging ? 1 : -1) * Math.Round(lift * 2) / 2; // Half-pixel steps keep the pixel art crisp.
        _image.Opacity = pose.Opacity;
        if (_effects != null)
        {
            // The mouth and tail are found through the sprite's own turn, flip, tilt and bob, so effects stay attached.
            var mouth = _image.TranslatePoint(new Point(_mouth.X * Side, _mouth.Y * Side), _effectsView);
            var tail = _image.TranslatePoint(new Point(_tail.X * Side, _tail.Y * Side), _effectsView);
            if (_effects.Update(seconds, mouth, tail, _motion.Direction)) _motion.Hold(_effects.AttackTime + .3);
            _effectsView!.InvalidateVisual();
        }
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        var top = pose.Hanging ? pose.Y : pose.Y - height;
        NativeMethods.SetWindowPos(_handle, new IntPtr(-1), (int)Math.Round(pose.X - width / 2.0), (int)Math.Round(top), 0, 0, 0x0001 | 0x0010);
    }
}
