using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Workspaces;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;

namespace WallpaperProfiles.UI;

/// <summary>Lays modules out in a row. Modules with long text give way first, and any that still do not fit are left out.</summary>
internal sealed class BarRow : System.Windows.Controls.Panel
{
    private const double Shortest = 70;
    private readonly HashSet<UIElement> _omitted = new();
    public double Spacing { get; set; }
    /// <summary>The width below which something has to be left out.</summary>
    public double Firm { get; private set; }
    public IReadOnlyCollection<UIElement> Omitted => _omitted;
    public static readonly DependencyProperty ShrinksProperty = DependencyProperty.RegisterAttached("Shrinks", typeof(bool), typeof(BarRow), new PropertyMetadata(false));

    private UIElement[] Shown => InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToArray();

    protected override System.Windows.Size MeasureOverride(System.Windows.Size available)
    {
        var shown = Shown;
        double used = 0, height = 0;
        Firm = Spacing * Math.Max(0, shown.Length - 1);
        _omitted.Clear();
        foreach (var child in InternalChildren.Cast<UIElement>().Where(child => !shown.Contains(child))) child.Measure(available);
        foreach (var child in shown) child.Measure(new System.Windows.Size(double.PositiveInfinity, available.Height));
        double Least(UIElement child) => (bool)child.GetValue(ShrinksProperty) ? Math.Min(child.DesiredSize.Width, Shortest) : child.DesiredSize.Width;
        var order = shown.OrderBy(child => (bool)child.GetValue(ShrinksProperty)).ToArray();
        // One long text leaves the other texts what they need.
        var reserved = order.Where(child => (bool)child.GetValue(ShrinksProperty)).Sum(child => Least(child) + Spacing);
        foreach (var child in order)
        {
            var shrinks = (bool)child.GetValue(ShrinksProperty);
            var spacing = used > 0 ? Spacing : 0;
            var room = Math.Max(0, available.Width - used - spacing);
            var wanted = child.DesiredSize.Width;
            var least = Least(child);
            Firm += least;
            if (shrinks) { reserved -= least + Spacing; room = Math.Max(Math.Min(room, least), room - reserved); }
            // Rounding to whole pixels may take a little off what was asked for.
            if (least > room + 1.5)
            {
                child.Measure(new System.Windows.Size(0, 0));
                _omitted.Add(child);
                continue;
            }
            if (wanted > room + 1.5) child.Measure(new System.Windows.Size(Math.Max(room, Math.Min(wanted, least)), available.Height));
            used += spacing + child.DesiredSize.Width; height = Math.Max(height, child.DesiredSize.Height);
        }
        return new System.Windows.Size(Math.Min(used, available.Width), height);
    }

    // The row may be able to have more room than it was given, even when its own width stays as it is.
    protected override void OnChildDesiredSizeChanged(UIElement child)
    {
        base.OnChildDesiredSizeChanged(child);
        (Parent as UIElement)?.InvalidateMeasure();
        ((Parent as FrameworkElement)?.Parent as UIElement)?.InvalidateMeasure();
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size final)
    {
        double x = 0;
        foreach (var child in Shown)
        {
            if (_omitted.Contains(child)) { child.Arrange(new Rect(0, 0, 0, 0)); continue; }
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, final.Height));
            x += child.DesiredSize.Width + Spacing;
        }
        return final;
    }
}

/// <summary>
/// The three parts of the bar. The middle one is centered, and moves aside only when a side would otherwise have to
/// leave something out. Long text alone does not move it.
/// </summary>
internal sealed class BarSections : System.Windows.Controls.Panel
{
    private readonly (Border Frame, BarRow Row) _start, _middle, _end;
    private double _startWidth, _endWidth;

    public BarSections((Border, BarRow) start, (Border, BarRow) middle, (Border, BarRow) end)
    {
        (_start, _middle, _end) = (start, middle, end);
        foreach (var part in new[] { _start, _middle, _end }) Children.Add(part.Frame);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size available)
    {
        double Wanted((Border Frame, BarRow Row) part, out double firm)
        {
            part.Frame.Measure(new System.Windows.Size(double.PositiveInfinity, available.Height));
            firm = part.Frame.DesiredSize.Width - part.Row.DesiredSize.Width + part.Row.Firm;
            return part.Frame.DesiredSize.Width;
        }
        var middle = Math.Min(Wanted(_middle, out _), available.Width);
        var rest = available.Width - middle;
        var half = rest / 2;
        var start = Math.Min(Wanted(_start, out var startFirm), Math.Max(half, startFirm));
        var end = Math.Min(Wanted(_end, out var endFirm), Math.Max(half, endFirm));
        if (start + end > rest)
        {
            if (start <= half) end = rest - start;
            else if (end <= half) start = rest - end;
            else start = end = half;
        }
        (_startWidth, _endWidth) = (start, end);
        _middle.Frame.Measure(new System.Windows.Size(middle, available.Height));
        _start.Frame.Measure(new System.Windows.Size(start, available.Height));
        _end.Frame.Measure(new System.Windows.Size(end, available.Height));
        return new System.Windows.Size(start + middle + end, Children.Cast<UIElement>().Max(child => child.DesiredSize.Height));
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size final)
    {
        var middle = _middle.Frame.DesiredSize.Width;
        var x = Math.Max(_startWidth, Math.Min((final.Width - middle) / 2, final.Width - _endWidth - middle));
        _start.Frame.Arrange(new Rect(0, 0, _start.Frame.DesiredSize.Width, final.Height));
        _middle.Frame.Arrange(new Rect(x, 0, middle, final.Height));
        _end.Frame.Arrange(new Rect(Math.Max(0, final.Width - _end.Frame.DesiredSize.Width), 0, _end.Frame.DesiredSize.Width, final.Height));
        return final;
    }
}

/// <summary>The docked bar on one monitor. It only displays state; <see cref="TopBarHost"/> decides what happens.</summary>
internal sealed class TopBarWindow : Window
{
    private sealed record View(BarModule Module, BarElement Element, BarFrame Frame, FrameworkElement Content, TextBlock? Icon, TextBlock? Text);

    private readonly Border _bar = new();
    private readonly BarFrame _left = new(), _center = new(), _right = new();
    private readonly BarRow _leftRow = new(), _centerRow = new(), _rightRow = new();
    private readonly StackPanel _buttonRow = new() { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
    private readonly Border _pill = new() { HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    private readonly Grid _workspaces = new() { Background = Brushes.Transparent };
    private readonly System.Diagnostics.Stopwatch _pillClock = new();
    private double _pillFromLeft, _pillFromRight, _pillLeft, _pillRight, _pillToLeft = double.NaN, _pillToRight;
    private readonly List<(Button Button, TextBlock Text)> _buttons = new();
    private readonly List<View> _views = new();
    private readonly Border _cover = new() { CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private Button? _previous, _toggle, _next;
    private readonly Popup _popup = new() { AllowsTransparency = true, StaysOpen = true, PopupAnimation = PopupAnimation.Fade };
    private readonly DispatcherTimer _popupClose = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private TopBarSettings _settings = new TopBarSettings().Normalize();
    private IReadOnlyList<WorkspaceState> _states = Array.Empty<WorkspaceState>();
    private string _titleText = "";
    private NowPlayingState? _playing;
    private SystemSnapshot? _reading;
    private WeatherReport? _weather;
    private bool _rebuilt;
    private AppBar? _dock;
    private IntPtr _handle;
    private System.Drawing.Rectangle _screen;
    private Brush _accent = Brushes.White, _ink = Brushes.Black, _text = Brushes.White, _fill = Brushes.Black, _border = Brushes.Gray;
    private BarStylesheet _sheet = BarStylesheet.Parse("");
    private string _css = "";
    private BarPalette _palette = new(Colors.Black, Colors.White, Colors.White, Colors.Gray);
    private BarElement _barElement = new("bar");
    private BarLook _barLook = new();
    private readonly Dictionary<BarModule, (string Text, string Tip)> _scripts = new();
    private readonly List<(Border Dot, double Width)> _dots = new();

    public string Monitor { get; }
    public event Action<int>? WorkspaceChosen;
    public event Action<int>? WorkspaceScrolled;
    public event Action<int>? WindowSent;
    public event Action<PlaybackCommand>? MediaCommand;
    public event Action<string>? LauncherClicked;
    public event Action? MuteClicked, SettingsClicked, HideClicked;
    public event Action<int>? VolumeScrolled;
    internal bool PopupOpen => _popup.IsOpen;
    internal Func<INowPlayingSource>? PlayerSource { get; set; }
    /// <summary>Shows made-up sound, for pictures of the bar that are not the bar.</summary>
    internal bool IsPreview { get; init; }
    /// <summary>What the stylesheet says that could not be understood.</summary>
    internal IReadOnlyList<string> StyleProblems => _sheet.Problems;

    /// <summary>What a workspace's button shows when hovered: how it looked when it was left, and its windows.</summary>
    internal Func<int, WorkspacePreview?>? Preview { get; set; }

    public TopBarWindow(string monitor)
    {
        Monitor = monitor;
        Title = "Belie top bar";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _workspaces.Children.Add(_pill); _workspaces.Children.Add(_buttonRow);
        _buttonRow.SizeChanged += (_, _) => MovePill(animate: false);
        _workspaces.MouseWheel += (_, e) => { WorkspaceScrolled?.Invoke(e.Delta > 0 ? -1 : 1); e.Handled = true; };
        _left.Child = _leftRow; _left.HorizontalAlignment = HorizontalAlignment.Left;
        _center.Child = _centerRow; _center.HorizontalAlignment = HorizontalAlignment.Center;
        _right.Child = _rightRow; _right.HorizontalAlignment = HorizontalAlignment.Right;
        _bar.Child = new BarSections((_left, _leftRow), (_center, _centerRow), (_right, _rightRow)); Content = _bar;
        _bar.VerticalAlignment = VerticalAlignment.Top;
        _bar.RenderTransform = _slide;
        _autoHide.Tick += (_, _) => CheckAutoHide();

        _popupClose.Tick += (_, _) =>
        {
            if (_popup.Child is { IsMouseOver: true } || _popup.PlacementTarget is { IsMouseOver: true } || _popup.Child is { IsMouseCaptureWithin: true }) return;
            ClosePopup();
        };

        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "Top bar settings" };
        settings.Click += (_, _) => SettingsClicked?.Invoke();
        var hide = new MenuItem { Header = "Turn off top bar" };
        hide.Click += (_, _) => HideClicked?.Invoke();
        menu.Items.Add(settings); menu.Items.Add(hide);
        ContextMenu = menu;

        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLong(_handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_handle, NativeMethods.GWL_EXSTYLE,
                (style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_APPWINDOW);
            _dock = new AppBar(HwndSource.FromHwnd(_handle));
            _dock.FullscreenChanged += fullscreen => { _shellFullscreen = fullscreen; CheckFullscreen(); };
            Place(_screen);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => Place(_screen));
        Closed += (_, _) => { _autoHide.Stop(); ClosePopup(); CompositionTarget.Rendering -= DrawPill; _dock?.Dispose(); };
    }

    private bool _shellFullscreen, _covered;

    // Auto-hide: the bar draws over windows and slides out of their way.
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Windows.Media.TranslateTransform _slide = new();
    private bool _tucked;
    private long? _atEdgeSince;
    private long _lastWanted;
    /// <summary>How long the pointer rests at the edge before the bar comes back, so a quick click on a tab there still reaches the app.</summary>
    private const int RevealDelay = 350;
    /// <summary>How long the bar stays after the pointer leaves it.</summary>
    private const int HideDelay = 400;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);
    private struct CursorPoint { public int X, Y; }

    private void CheckAutoHide()
    {
        if (_screen.IsEmpty || _handle == IntPtr.Zero || !GetCursorPos(out var cursor)) return;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var strip = (int)Math.Round(FullHeight * scale);
        var bottom = _settings.Edge == TopBarEdge.Bottom;
        var bar = bottom ? System.Drawing.Rectangle.FromLTRB(_screen.Left, _screen.Bottom - strip, _screen.Right, _screen.Bottom)
            : new System.Drawing.Rectangle(_screen.X, _screen.Y, _screen.Width, strip);
        var point = new System.Drawing.Point(cursor.X, cursor.Y);
        var now = Environment.TickCount64;
        var atEdge = point.X >= _screen.Left && point.X < _screen.Right && (bottom ? point.Y >= _screen.Bottom - 2 : point.Y <= _screen.Top + 1);
        _atEdgeSince = atEdge ? _atEdgeSince ?? now : null;
        var busy = bar.Contains(point) && !_tucked || _popup.IsOpen || ContextMenu?.IsOpen == true;
        if (busy || !Underneath(bar) || _atEdgeSince is { } since && now - since >= RevealDelay) _lastWanted = now;
        Tuck(now - _lastWanted > (_tucked ? 0 : HideDelay));
    }

    /// <summary>Whether the window in front reaches under the bar on this monitor.</summary>
    private bool Underneath(System.Drawing.Rectangle bar)
    {
        var front = NativeMethods.GetForegroundWindow();
        if (front == IntPtr.Zero || front == _handle || front == NativeMethods.GetShellWindow() || !NativeMethods.IsWindowVisible(front) || NativeMethods.IsIconic(front)) return false;
        if (ShellClasses.Contains(NativeMethods.ClassName(front)) || !NativeMethods.GetWindowRect(front, out var rect)) return false;
        return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom).IntersectsWith(bar);
    }

    /// <summary>Slides the bar out of view, or back. Out of view, its window is see-through, so clicks reach what is under it.</summary>
    private void Tuck(bool tucked)
    {
        if (tucked == _tucked) return;
        _tucked = tucked;
        if (tucked) ClosePopup();
        var away = (FullHeight + 4) * (_settings.Edge == TopBarEdge.Bottom ? 1 : -1);
        var to = tucked ? away : 0;
        _bar.IsHitTestVisible = !tucked;
        if (_settings.AnimationsEnabled && SystemParameters.ClientAreaAnimation)
            _slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        else { _slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null); _slide.Y = to; }
    }

    private void UpdateAutoHide()
    {
        if (_settings.AutoHide && !IsPreview) { _autoHide.Start(); return; }
        _autoHide.Stop();
        _lastWanted = Environment.TickCount64;
        Tuck(false);
    }

    /// <summary>
    /// Steps aside for a full-screen app on this monitor. Windows also reports "full screen" when the desktop itself
    /// gets focus (closing the last window does that), so its word is checked against the window actually in front.
    /// Called on the shell's notice and again whenever the foreground changes.
    /// </summary>
    internal void CheckFullscreen()
    {
        var covered = false;
        if (_shellFullscreen)
        {
            var front = NativeMethods.GetForegroundWindow();
            covered = front != IntPtr.Zero && front != NativeMethods.GetShellWindow()
                && WallpaperProfiles.Workspaces.NativeWindowSystem.MonitorOf(front) == Monitor
                && NativeMethods.GetWindowRect(front, out var rect) && MonitorBounds(front) is { } monitor
                && IsFullscreenApp(NativeMethods.ClassName(front),
                    System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom), monitor);
        }
        if (covered == _covered) return;
        _covered = covered;
        Topmost = !covered;
        // Nothing of the bar is seen, so a game gets its share of the machine too.
        _bar.Visibility = covered ? Visibility.Hidden : Visibility.Visible;
        if (covered)
        {
            ClosePopup();
            NativeMethods.SetWindowPos(_handle, NativeMethods.HwndBottom, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private static System.Drawing.Rectangle? MonitorBounds(IntPtr window)
    {
        var info = new NativeMethods.MONITORINFOEX { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info)) return null;
        return System.Drawing.Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
    }

    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// <summary>Whether a window is a real app filling its whole monitor, rather than the desktop or the taskbar.</summary>
    internal static bool IsFullscreenApp(string className, System.Drawing.Rectangle window, System.Drawing.Rectangle monitor)
        => className.Length > 0 && !ShellClasses.Contains(className) && !monitor.IsEmpty && window.Contains(monitor);

    private double FullHeight => _settings.Height + _settings.BarMargin;
    /// <summary>Room past the bar for what reaches over other windows. It is see-through, and clicks go through it.</summary>
    private double Overhang => _settings.Shows(BarModuleKind.Island) ? Math.Ceiling(_settings.Height * _settings.IslandGrowth + 8) : 0;

    /// <summary>Docks to a monitor, given its bounds in physical pixels.</summary>
    public void Place(System.Drawing.Rectangle screen)
    {
        _screen = screen;
        if (_dock == null || screen.IsEmpty) return;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        _dock.Dock(screen, (int)Math.Round(FullHeight * scale), _settings.Edge == TopBarEdge.Bottom, (int)Math.Ceiling(Overhang * scale), !_settings.AutoHide);
    }

    /// <summary>Follows resolution and arrangement changes without redocking every second.</summary>
    public void PlaceIfMoved(System.Drawing.Rectangle screen) { if (screen != _screen) Place(screen); }

    /// <param name="colors">Colors taken from the wallpaper, used when the settings ask for them.</param>
    public void Apply(TopBarSettings settings, WallpaperColors? colors = null)
    {
        if (settings.Left == null || settings.Center == null || settings.Right == null) settings.Normalize();
        var before = (FullHeight, Overhang, _settings.Edge, _settings.AutoHide);
        _settings = settings;
        var moved = before != (FullHeight, Overhang, _settings.Edge, _settings.AutoHide);
        if (_css != settings.Css) { _css = settings.Css; _sheet = BarStylesheet.Parse(_css); }
        ClosePopup();
        var matched = settings.Colors == TopBarColors.Wallpaper ? colors : null;
        var background = matched?.Background ?? Parse(settings.BackgroundColor, "#111613");
        var accent = matched?.Accent ?? Parse(settings.AccentColor, "#DDF79D");
        var textColor = matched?.Text ?? Parse(settings.TextColor, "#F3F5EC");
        var borderColor = matched?.Accent ?? Parse(settings.BorderColor, "#364239");
        _palette = new BarPalette(background, textColor, accent, borderColor);
        _palette.Define(_sheet.Colors);
        // A stylesheet may redefine these too.
        (background, textColor, accent, borderColor) = (_palette["background"], _palette["text"], _palette["accent"], _palette["border"]);
        _accent = Frozen(accent); _text = Frozen(textColor); _ink = InkFor(accent);
        background.A = (byte)Math.Round(background.A * settings.Opacity);
        _fill = Frozen(background);
        var border = _border = Frozen(borderColor);
        _barElement = new BarElement("bar", "", settings.Edge == TopBarEdge.Bottom ? "bottom" : "top");
        _barLook = Look(settings.BarStyle, _barElement);
        Height = FullHeight + Overhang;
        FontFamily = BarFonts.Find(_barLook.Font ?? settings.FontFamily); FontSize = _barLook.Size ?? settings.FontSize; Foreground = _barLook.Color ?? _text;
        FontWeight = _barLook.Weight ?? (settings.Bold ? FontWeights.SemiBold : FontWeights.Normal);
        // Also on the bar itself, which can be shown without its window.
        _bar.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, FontFamily); _bar.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, FontSize);
        _bar.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Foreground); _bar.SetValue(System.Windows.Documents.TextElement.FontWeightProperty, FontWeight);
        _bar.Height = settings.Height; _bar.Opacity = _barLook.Opacity ?? 1;
        var solid = settings.Style == TopBarStyle.Solid;
        var floating = settings.BarMargin > 0;
        var margin = settings.BarMargin;
        var bottom = settings.Edge == TopBarEdge.Bottom;
        _bar.Margin = floating ? new Thickness(margin, bottom ? 0 : margin, margin, bottom ? margin : 0) : new Thickness(0);
        _bar.Background = _barLook.Background ?? (solid ? _fill : Brushes.Transparent);
        _bar.CornerRadius = _barLook.Radius ?? new CornerRadius(solid && floating ? settings.CornerRadius : 0);
        _bar.BorderBrush = _barLook.BorderColor ?? border;
        _bar.BorderThickness = _barLook.Border ?? (!solid ? new Thickness(0) : floating ? new Thickness(settings.BorderWidth)
            : bottom ? new Thickness(0, settings.BorderWidth, 0, 0) : new Thickness(0, 0, 0, settings.BorderWidth));
        _bar.Padding = _barLook.Padding ?? (solid ? new Thickness(4, 0, 4, 0) : floating ? new Thickness(0)
            : bottom ? new Thickness(8, 2, 8, 4) : new Thickness(8, 4, 8, 2));
        _bar.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        foreach (var (section, row, name, style) in new[] { (_left, _leftRow, "left", settings.LeftStyle), (_center, _centerRow, "center", settings.CenterStyle), (_right, _rightRow, "right", settings.RightStyle) })
        {
            var element = new BarElement("part", "", name, _barElement);
            var look = Look(style, element);
            var shape = look.Shape ?? (settings.Style == TopBarStyle.Islands ? BarShape.Island : BarShape.None);
            Frame(section, shape != BarShape.None, border);
            section.Padding = new Thickness(settings.Style == TopBarStyle.Pills ? 0 : 10, 0, settings.Style == TopBarStyle.Pills ? 0 : 10, 0);
            section.Margin = section == _center ? new Thickness(8, 0, 8, 0) : new Thickness(0);
            Dress(section, look);
            section.Shape(shape == BarShape.Notch, bottom);
            if (shape == BarShape.Notch)
            {
                // Joined to the screen edge, with room at the sides for the corners that curve out.
                var reach = section.CornerRadius.BottomLeft > 0 ? section.CornerRadius.BottomLeft : section.CornerRadius.TopLeft;
                var edge = bottom ? _bar.Padding.Bottom + _bar.Margin.Bottom + _bar.BorderThickness.Bottom : _bar.Padding.Top + _bar.Margin.Top + _bar.BorderThickness.Top;
                section.Margin = new Thickness(Math.Max(reach, section.Margin.Left), bottom ? section.Margin.Top : -edge, Math.Max(reach, section.Margin.Right), bottom ? -edge : section.Margin.Bottom);
            }
            row.Spacing = look.Spacing ?? (settings.Style == TopBarStyle.Pills ? Math.Max(4, settings.ModuleSpacing / 2) : settings.ModuleSpacing);
        }
        BuildModules(border);
        if (moved) Place(_screen);
        UpdateAutoHide();
    }

    private BarLook Look(IEnumerable<KeyValuePair<string, string>> own, BarElement element)
        => BarLook.From(own.Concat(_sheet.For(element)), _palette, _settings.FontSize);

    private void Frame(BarFrame frame, bool visible, Brush border)
    {
        frame.Background = visible ? _fill : Brushes.Transparent;
        frame.CornerRadius = new CornerRadius(visible ? _settings.CornerRadius : 0);
        frame.BorderBrush = border;
        frame.BorderThickness = new Thickness(visible ? _settings.BorderWidth : 0);
        frame.Opacity = 1;
    }

    /// <summary>Puts what the styles say over what the frame has.</summary>
    private static void Dress(BarFrame frame, BarLook look)
    {
        if (look.Background != null) frame.Background = look.Background;
        if (look.BorderColor != null) frame.BorderBrush = look.BorderColor;
        if (look.Border is { } border) frame.BorderThickness = border;
        if (look.Radius is { } radius) frame.CornerRadius = radius;
        if (look.Padding is { } padding) frame.Padding = padding;
        if (look.Margin is { } margin) frame.Margin = margin;
        if (look.Opacity is { } opacity) frame.Opacity = opacity;
        frame.MinWidth = look.MinWidth ?? 0;
        frame.Width = look.Width ?? double.NaN;
        frame.Height = look.Height ?? double.NaN;
        frame.VerticalAlignment = look.Height != null ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        void Inherit(DependencyProperty property, object? value) { if (value != null) frame.SetValue(property, value); else frame.ClearValue(property); }
        Inherit(System.Windows.Documents.TextElement.ForegroundProperty, look.Color);
        Inherit(System.Windows.Documents.TextElement.FontSizeProperty, look.Size);
        Inherit(System.Windows.Documents.TextElement.FontFamilyProperty, look.Font is { Length: > 0 } font ? BarFonts.Find(font) : null);
        Inherit(System.Windows.Documents.TextElement.FontWeightProperty, look.Weight);
        Inherit(System.Windows.Documents.TextElement.FontStyleProperty, look.Italic == true ? FontStyles.Italic : null);
    }

    private void DressModule(View view, bool hovered)
    {
        var element = hovered ? view.Element with { Classes = view.Element.Classes + " hover" } : view.Element;
        var look = Look(view.Module.Style, element);
        var pills = _settings.Style == TopBarStyle.Pills && view.Module.Kind is not (BarModuleKind.Island or BarModuleKind.Separator);
        Frame(view.Frame, pills || look.Shape is BarShape.Island, _border);
        var side = !pills ? 0 : view.Module.Kind == BarModuleKind.Workspaces ? 6 : 12;
        view.Frame.Padding = new Thickness(side, 0, side, 0);
        view.Frame.Margin = new Thickness(0);
        if (view.Content is BarIsland island)
        {
            island.Apply(_settings, look, _fill, Foreground, _accent, Math.Max(10, _settings.Height - _bar.Padding.Top - _bar.Padding.Bottom), IsPreview);
            return;
        }
        Dress(view.Frame, look);
        if (view.Icon == null) return;
        view.Icon.Foreground = look.IconColor ?? look.Color ?? _accent;
        view.Icon.FontSize = look.Size ?? FontSize;
        var icon = Look(new Dictionary<string, string>(), new BarElement("icon", "", hovered ? "hover" : "", element));
        if (icon.Color != null) view.Icon.Foreground = icon.Color;
        if (icon.Size != null) view.Icon.FontSize = icon.Size.Value;
        if (icon.Margin != null) view.Icon.Margin = icon.Margin.Value;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void BuildModules(Brush border)
    {
        foreach (var view in _views) { view.Frame.Child = null; }
        _dots.Clear();
        if (_cover.Parent is System.Windows.Controls.Panel old) old.Children.Remove(_cover);
        _views.Clear(); _buttons.Clear(); _buttonRow.Children.Clear();
        _previous = _toggle = _next = null;
        foreach (var gone in _scripts.Keys.Except(_settings.Modules).ToArray()) _scripts.Remove(gone);
        foreach (var (row, modules, part) in new[] { (_leftRow, _settings.Left!, "left"), (_centerRow, _settings.Center!, "center"), (_rightRow, _settings.Right!, "right") })
        {
            row.Children.Clear();
            var parent = new BarElement("part", "", part, _barElement);
            foreach (var module in modules)
            {
                var kind = module.Kind.ToString().ToLowerInvariant();
                var view = Build(module, module.Name.Length > 0 ? new BarElement("module", "custom-" + module.Name, $"module {kind} {module.Name}", parent)
                    : new BarElement("module", kind, "module " + kind, parent));
                DressModule(view, hovered: false);
                AddCommands(view);
                if (_sheet.Styles(view.Element with { Classes = view.Element.Classes + " hover" }))
                {
                    view.Frame.MouseEnter += (_, _) => DressModule(view, hovered: true);
                    view.Frame.MouseLeave += (_, _) => DressModule(view, hovered: false);
                }
                view.Frame.SetValue(BarRow.ShrinksProperty, module.Kind is BarModuleKind.Title or BarModuleKind.Media or BarModuleKind.Text);
                _views.Add(view); row.Children.Add(view.Frame);
            }
        }
        UpdateClock(DateTime.Now);
        ShowMedia(); ShowSystem(); ShowWeather(); ShowTitle(); ShowScripts();
        BuildWorkspaces();
    }

    /// <summary>Commands of a module's own come before what the module does by itself.</summary>
    private void AddCommands(View view)
    {
        var (module, frame) = (view.Module, view.Frame);
        void Run(string command, RoutedEventArgs e) { e.Handled = true; LauncherClicked?.Invoke(command); }
        if (module.OnClick.Length > 0 && module.Kind != BarModuleKind.Workspaces)
        {
            frame.Cursor = Cursors.Hand;
            frame.PreviewMouseLeftButtonUp += (_, e) => Run(module.OnClick, e);
        }
        if (module.OnRightClick.Length > 0) frame.PreviewMouseRightButtonUp += (_, e) => Run(module.OnRightClick, e);
        if (module.OnMiddleClick.Length > 0) frame.PreviewMouseUp += (_, e) => { if (e.ChangedButton == System.Windows.Input.MouseButton.Middle) Run(module.OnMiddleClick, e); };
        if (module.OnScrollUp.Length + module.OnScrollDown.Length > 0)
            frame.PreviewMouseWheel += (_, e) => { var command = e.Delta > 0 ? module.OnScrollUp : module.OnScrollDown; if (command.Length > 0) Run(command, e); };
    }

    private View Build(BarModule module, BarElement element)
    {
        var name = "TopBar" + module.Kind;
        var text = new TextBlock { Name = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        var frame = new BarFrame { Background = Brushes.Transparent };
        TextBlock? icon = null;
        FrameworkElement content = text;
        if (IconFor(module) is { Length: > 0 } glyph)
        {
            icon = new TextBlock { Text = glyph, FontFamily = module.Icon.Length > 0 ? BarFonts.Find(_settings.FontFamily) : new FontFamily(module.Kind == BarModuleKind.Weather ? "Segoe UI Symbol" : "Segoe MDL2 Assets"),
                FontSize = _settings.FontSize, FontWeight = FontWeights.Normal, Foreground = _accent, VerticalAlignment = VerticalAlignment.Center,
                // An icon with nothing beside it needs no space there.
                Margin = new Thickness(0, 0, module.Kind is BarModuleKind.Wallpaper && module.Text.Length == 0 || module.Kind is BarModuleKind.Launcher && module.Text.Length == 0 || module.Format == "{icon}" ? 0 : 7, 0) };
            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon); row.Children.Add(text);
            content = row;
        }
        switch (module.Kind)
        {
            case BarModuleKind.Workspaces:
                if (_workspaces.Parent is Border previous) previous.Child = null;
                content = _workspaces;
                break;
            case BarModuleKind.Media:
                content = BuildMedia(text);
                frame.MaxWidth = _settings.MediaWidth;
                break;
            case BarModuleKind.Visualizer:
                var look = Look(module.Style, element);
                var spectrum = new AudioSpectrumView { Bars = _settings.VisualizerBars, PreviewMode = IsPreview, Width = _settings.VisualizerWidth, Height = Math.Max(8, _settings.Height - 18), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                spectrum.UpdateAppearance(true, look.Color ?? _accent, new DesktopWidget { SpectrumSkin = _settings.VisualizerSkin });
                content = spectrum;
                break;
            case BarModuleKind.Island:
                var island = new BarIsland();
                island.MouseLeftButtonUp += (_, _) =>
                {
                    if (_playing is { HasSession: true } && _settings.IslandShowsMusic) OpenPlayer(island.Frame);
                    else OpenCalendar(island.Frame);
                };
                content = island;
                break;
            case BarModuleKind.Separator:
                if (module.Text.Length > 0) text.Text = module.Text;
                else content = new Border { Width = 1.5, Height = Math.Max(6, _settings.Height * .42), CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center,
                    Background = Look(module.Style, element).Color ?? _text, Opacity = .3 };
                break;
            case BarModuleKind.Wallpaper:
                text.Text = module.Text;
                text.Visibility = Show(module.Text.Length > 0);
                frame.Cursor = Cursors.Hand;
                frame.ToolTip = "Choose a wallpaper, or scroll through them";
                frame.MouseLeftButtonUp += (_, _) => LauncherClicked?.Invoke("belie:wallpapers");
                frame.MouseWheel += (_, e) => { LauncherClicked?.Invoke(e.Delta > 0 ? "belie:previous-wallpaper" : "belie:next-wallpaper"); e.Handled = true; };
                break;
            case BarModuleKind.Script:
                text.Text = "…";
                break;
            case BarModuleKind.Text:
                text.Text = module.Text;
                break;
            case BarModuleKind.Launcher:
                text.Text = module.Text.Length > 0 || icon != null ? module.Text : "Open";
                text.Visibility = Show(text.Text.Length > 0);
                frame.Cursor = Cursors.Hand;
                frame.ToolTip = module.Command;
                frame.MouseLeftButtonUp += (_, _) => { if (module.Command.Length > 0) LauncherClicked?.Invoke(module.Command); };
                break;
            case BarModuleKind.Volume:
                frame.Cursor = Cursors.Hand;
                frame.ToolTip = "Click to mute, scroll to change the volume";
                frame.MouseWheel += (_, e) => { VolumeScrolled?.Invoke(e.Delta > 0 ? 2 : -2); e.Handled = true; };
                frame.MouseLeftButtonUp += (_, _) => MuteClicked?.Invoke();
                break;
            case BarModuleKind.Clock or BarModuleKind.Date:
                frame.Cursor = Cursors.Hand;
                frame.ToolTip = "Show the calendar";
                frame.MouseLeftButtonUp += (_, _) => OpenCalendar(frame);
                break;
            case BarModuleKind.Weather:
                frame.Cursor = Cursors.Hand;
                frame.MouseLeftButtonUp += (_, _) =>
                {
                    if (_weather == null) return;
                    var widget = PopupWidget(DesktopWidgetKind.Weather, 320, 210);
                    widget.Content = _settings.WeatherPlace; widget.WeatherFahrenheit = _settings.WeatherFahrenheit; widget.WeatherSkin = WeatherSkin.Forecast;
                    TogglePopup(frame, () => new WeatherWidgetView(widget), 320, 210);
                };
                break;
        }
        frame.Child = content;
        return new View(module, element, frame, content, icon, text);
    }

    private string IconFor(BarModule module) => module.Icon switch
    {
        "-" => "",
        { Length: > 0 } own => own,
        _ when module.Kind is BarModuleKind.Island or BarModuleKind.Workspaces or BarModuleKind.Separator or BarModuleKind.Visualizer => "",
        _ => _settings.ShowIcons || module.Kind == BarModuleKind.Wallpaper ? Glyph(module.Kind) : ""
    };

    private void OpenCalendar(FrameworkElement target) => TogglePopup(target, () => new CalendarWidgetView(PopupWidget(DesktopWidgetKind.Calendar, 260, 270)), 260, 270);

    private void OpenPlayer(FrameworkElement target)
    {
        var (width, height) = PlayerSize(_settings.PlayerLayout);
        TogglePopup(target, () => new NowPlayingView(PopupWidget(DesktopWidgetKind.NowPlaying, width, height), PlayerSource), width, height,
            padded: !NowPlayingView.FillsCard(_settings.PlayerLayout));
    }

    private FrameworkElement BuildMedia(TextBlock text)
    {
        var row = new DockPanel { LastChildFill = true };
        if (_settings.MediaStyle != BarMediaStyle.Text)
        {
            _cover.Width = _cover.Height = Math.Max(14, _settings.Height - 14);
            DockPanel.SetDock(_cover, Dock.Left); row.Children.Add(_cover);
        }
        if (_settings.MediaStyle == BarMediaStyle.Controls)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            buttons.Children.Add(_previous = MediaButton("\uE892", "Previous track", PlaybackCommand.Previous));
            buttons.Children.Add(_toggle = MediaButton("\uE768", "Play or pause", PlaybackCommand.Toggle));
            buttons.Children.Add(_next = MediaButton("\uE893", "Next track", PlaybackCommand.Next));
            DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons);
        }
        var label = new Border { Background = Brushes.Transparent, Child = text, Cursor = Cursors.Hand };
        if (_settings.MediaStyle == BarMediaStyle.CoverOnly) { label.Visibility = Visibility.Collapsed; _cover.Margin = new Thickness(0); }
        else _cover.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(label);
        foreach (var target in new[] { label, _cover })
        {
            target.MouseLeftButtonUp -= MediaClicked;
            target.MouseLeftButtonUp += MediaClicked;
        }
        _cover.Cursor = Cursors.Hand;
        label.ToolTip = _settings.MediaClick == BarMediaClick.PlayPause ? "Play or pause" : "Open the player";
        row.MouseWheel += (_, e) => { MediaCommand?.Invoke(e.Delta > 0 ? PlaybackCommand.Previous : PlaybackCommand.Next); e.Handled = true; };
        return row;
    }

    private void MediaClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_settings.MediaClick == BarMediaClick.PlayPause) { MediaCommand?.Invoke(PlaybackCommand.Toggle); return; }
        var view = _views.FirstOrDefault(item => item.Module.Kind == BarModuleKind.Media);
        if (view != null) OpenPlayer(view.Frame);
    }

    internal static (double Width, double Height) PlayerSize(NowPlayingLayout layout) => layout switch
    {
        NowPlayingLayout.Cover => (320, 320), NowPlayingLayout.Ring => (280, 340), NowPlayingLayout.Stacked => (300, 380),
        NowPlayingLayout.Vinyl => (380, 210), NowPlayingLayout.Pill => (420, 76), _ => (360, 200)
    };

    private Button MediaButton(string glyph, string label, PlaybackCommand command)
    {
        var button = new Button
        {
            Style = new Style(typeof(Button)), MinHeight = 0, Padding = new Thickness(0), Width = _settings.FontSize * 2, Template = PillTemplate(),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false, Cursor = Cursors.Hand, ToolTip = label,
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontWeight = FontWeights.Normal, FontSize = _settings.FontSize * .85,
                Foreground = _text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, e) => { MediaCommand?.Invoke(command); e.Handled = true; };
        return button;
    }

    private DesktopWidget PopupWidget(DesktopWidgetKind kind, double width, double height) => new()
    {
        Kind = kind, Width = width, Height = height, Locked = true, ShowHeader = false, MusicLayout = _settings.PlayerLayout, MusicColorsFromArt = true, MusicArtBackdrop = true,
        FontFamily = _settings.FontFamily, FontSize = kind == DesktopWidgetKind.NowPlaying ? 18 : 14, Alignment = WidgetTextAlignment.Left,
        TextColor = Hex(_text), MusicAccentColor = Hex(_accent)
    };

    private static string Hex(Brush brush) => brush is SolidColorBrush { Color: var color } ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : "#FFFFFF";

    private void TogglePopup(FrameworkElement target, Func<FrameworkElement> create, double width, double height, bool padded = true)
    {
        var open = _popup.IsOpen && _popup.PlacementTarget == target;
        ClosePopup();
        if (open) return;
        var content = create();
        content.Width = padded ? width - 32 : width; content.Height = padded ? height - 32 : height;
        var look = Look(new Dictionary<string, string>(), new BarElement("popup"));
        _popup.Child = new Border
        {
            Background = look.Background ?? Frozen(Color.FromArgb(248, ((SolidColorBrush)_fill).Color.R, ((SolidColorBrush)_fill).Color.G, ((SolidColorBrush)_fill).Color.B)),
            CornerRadius = look.Radius ?? new CornerRadius(16), BorderBrush = look.BorderColor ?? _accent, BorderThickness = look.Border ?? new Thickness(Math.Max(0, _settings.BorderWidth)),
            Padding = new Thickness(padded ? 16 : 0), Child = content, Margin = look.Margin ?? new Thickness(8)
        };
        foreach (var (property, value) in new (DependencyProperty, object)[] { (System.Windows.Documents.TextElement.ForegroundProperty, _text),
            (System.Windows.Documents.TextElement.FontFamilyProperty, FontFamily) }) ((Border)_popup.Child).SetValue(property, value);
        _popup.PlacementTarget = target;
        _popup.Placement = _settings.Edge == TopBarEdge.Bottom ? PlacementMode.Top : PlacementMode.Bottom;
        _popup.IsOpen = true;
        _popupClose.Start();
    }

    internal void ClosePopup()
    {
        _popupClose.Stop();
        _popup.IsOpen = false;
        _popup.Child = null; // Stops players and timers inside it.
        _popup.PlacementTarget = null;
    }

    private static string Glyph(BarModuleKind kind) => kind switch
    {
        BarModuleKind.Cpu => "\uE950", BarModuleKind.Memory => "\uE964", BarModuleKind.Disk => "\uEDA2", BarModuleKind.Network => "\uE968",
        BarModuleKind.Volume => "\uE767", BarModuleKind.Battery => "\uE83F", BarModuleKind.Uptime => "\uE823", BarModuleKind.Weather => "☀",
        BarModuleKind.Wallpaper => "\uE8B9", _ => ""
    };

    private void BuildWorkspaces()
    {
        _buttons.Clear(); _buttonRow.Children.Clear();
        var show = _settings.WorkspacesEnabled && _views.Any(view => view.Module.Kind == BarModuleKind.Workspaces);
        for (var index = 1; index <= (show ? _settings.WorkspaceCount : 0); index++)
        {
            var target = index;
            var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var dot = _settings.WorkspaceLabels == WorkspaceLabelStyle.Pills ? new Border { Width = _settings.WorkspaceDot, Height = _settings.WorkspaceDot, VerticalAlignment = VerticalAlignment.Center } : null;
            if (dot != null) _dots.Add((dot, _settings.WorkspaceDot));
            var button = new Button
            {
                // Belie's own button style is sized for dialogs and would overflow the bar.
                Style = new Style(typeof(Button)), MinHeight = 0, Padding = new Thickness(0),
                Content = (FrameworkElement?)dot ?? text, Focusable = false, Cursor = Cursors.Hand, Template = PillTemplate(dot == null ? 6 : _settings.WorkspaceSpacing / 2), MinWidth = dot == null ? _settings.FontSize * 2 : 0,
                Margin = dot == null ? new Thickness(1, 4, 1, 4) : new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0)
            };
            AutomationProperties.SetAutomationId(button, "Workspace" + index);
            button.Click += (_, _) => WorkspaceChosen?.Invoke(target);
            button.ToolTip = new System.Windows.Controls.ToolTip();
            button.ToolTipOpening += (_, _) => FillPreview(button, target);
            ToolTipService.SetInitialShowDelay(button, 250);
            var menu = new ContextMenu();
            var send = new MenuItem { Header = $"Send current window to workspace {index}" };
            send.Click += (_, _) => WindowSent?.Invoke(target);
            menu.Items.Add(send);
            button.ContextMenu = menu;
            _buttonRow.Children.Add(button); _buttons.Add((button, text));
        }
        _pill.CornerRadius = new CornerRadius(Math.Min(7, Math.Max(2, _settings.CornerRadius)));
        _pill.Visibility = Show(_dots.Count == 0);
        _rebuilt = true;
        UpdateWorkspaces(_states);
        UpdateSections();
    }

    private void FillPreview(Button button, int index)
    {
        if (button.ToolTip is not System.Windows.Controls.ToolTip tip) return;
        var state = _states.FirstOrDefault(item => item.Index == index);
        var name = _settings.WorkspaceName(index) is { Length: > 0 } given ? $"Workspace {index}, {given}" : $"Workspace {index}";
        var preview = Preview?.Invoke(index);
        var panel = new StackPanel { MaxWidth = 320 };
        if (preview?.Picture is { } picture && state.WindowCount > 0)
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 6),
                Child = new System.Windows.Controls.Image { Source = picture, Width = 320, Stretch = Stretch.Uniform }
            });
        panel.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
        var titles = preview?.Titles ?? Array.Empty<string>();
        if (titles.Count == 0) panel.Children.Add(new TextBlock { Text = state.IsActive ? "You are here" : "Empty", Opacity = .7 });
        foreach (var title in titles.Take(6))
            panel.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .85 });
        if (titles.Count > 6) panel.Children.Add(new TextBlock { Text = $"and {titles.Count - 6} more", Opacity = .7 });
        tip.Content = panel;
    }

    private static ControlTemplate PillTemplate(double side = 6)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background))
        { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.PaddingProperty, new Thickness(side, 0, side, 0));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    internal static string WorkspaceLabel(TopBarSettings settings, WorkspaceState state) => settings.WorkspaceLabels switch
    {
        WorkspaceLabelStyle.Dots => state.IsActive || state.WindowCount > 0 ? "●" : "○",
        WorkspaceLabelStyle.Roman => new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" }[Math.Clamp(state.Index, 1, 9) - 1],
        WorkspaceLabelStyle.Names when settings.WorkspaceName(state.Index) is { Length: > 0 } name => name,
        _ => state.Index.ToString(CultureInfo.InvariantCulture)
    };

    public void UpdateWorkspaces(IReadOnlyList<WorkspaceState> states)
    {
        if (!_rebuilt && _states.SequenceEqual(states)) return;
        _states = states; _rebuilt = false;
        foreach (var state in states)
        {
            if (state.Index > _buttons.Count) continue;
            var (button, text) = _buttons[state.Index - 1];
            text.Text = WorkspaceLabel(_settings, state);
            var look = WorkspaceLook(state);
            text.Foreground = look.Color ?? (state.IsActive ? _ink : Foreground);
            text.FontWeight = look.Weight ?? (state.IsActive ? FontWeights.SemiBold : FontWeights.Normal);
            text.Opacity = look.Opacity ?? (state.IsActive || state.WindowCount > 0 ? 1 : .45);
            if (look.Size is { } size) text.FontSize = size;
            if (_dots.Count >= state.Index) ShowDot(state, look);
            else if (!state.IsActive && look.Background != null) button.Background = look.Background;
            else button.Background = Brushes.Transparent;
            if (state.IsActive) _pill.Background = look.Background ?? _accent;
            if (look.Margin is { } margin) button.Margin = margin;
            button.Visibility = Show(!_settings.HideEmptyWorkspaces || state.IsActive || state.WindowCount > 0);
            var windows = state.WindowCount == 1 ? "1 window" : $"{state.WindowCount} windows";
            var name = _settings.WorkspaceName(state.Index) is { Length: > 0 } given ? $"Workspace {state.Index}, {given}" : $"Workspace {state.Index}";
            AutomationProperties.SetName(button, $"{name}, {windows}" + (state.IsActive ? ", open" : ""));
        }
        if (_settings.HideEmptyWorkspaces && _buttonRow.IsLoaded) _buttonRow.UpdateLayout(); // Otherwise the pill would aim at where a button used to be.
        MovePill(animate: true);
        var open = states.FirstOrDefault(state => state.IsActive).Index;
        foreach (var view in Of(BarModuleKind.Island))
            ((BarIsland)view.Content).UpdateWorkspace(open, _settings.WorkspaceName(open) is { Length: > 0 } label ? label : $"Workspace {open}");
    }

    private BarLook WorkspaceLook(WorkspaceState state)
    {
        var parent = _views.FirstOrDefault(view => view.Module.Kind == BarModuleKind.Workspaces)?.Element;
        return Look(new Dictionary<string, string>(), new BarElement("button", "", state.IsActive ? "active" : state.WindowCount > 0 ? "occupied" : "empty", parent));
    }

    /// <summary>A dot that stretches while its workspace is open.</summary>
    private void ShowDot(WorkspaceState state, BarLook look)
    {
        var (dot, before) = _dots[state.Index - 1];
        var width = look.Width ?? look.MinWidth ?? (state.IsActive ? Math.Max(_settings.WorkspaceDot, _settings.WorkspaceDotOpen) : _settings.WorkspaceDot);
        dot.Height = look.Height ?? _settings.WorkspaceDot;
        dot.CornerRadius = look.Radius ?? new CornerRadius(Math.Min(width, dot.Height) / 2);
        dot.Background = look.Background ?? (state.IsActive ? _accent : Foreground);
        dot.Opacity = look.Opacity ?? (state.IsActive ? 1 : state.WindowCount > 0 ? .8 : .35);
        if (before == width) { if (!dot.HasAnimatedProperties) dot.Width = width; return; }
        _dots[state.Index - 1] = (dot, width);
        if (!_settings.AnimationsEnabled || !SystemParameters.ClientAreaAnimation || !dot.IsLoaded) { dot.BeginAnimation(WidthProperty, null); dot.Width = width; return; }
        dot.BeginAnimation(WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(width, TimeSpan.FromMilliseconds(Motion.OpenTime / _settings.AnimationSpeed))
        { EasingFunction = new System.Windows.Media.Animation.BackEase { Amplitude = .35, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    private void MovePill(bool animate)
    {
        _pill.Margin = new Thickness(_pillLeft, 4, 0, 4);
        var active = _states.FirstOrDefault(state => state.IsActive).Index;
        if (active < 1 || active > _buttons.Count || _buttons[active - 1].Button is not { ActualWidth: > 0 } button) return;
        var left = button.TranslatePoint(new System.Windows.Point(), _buttonRow).X;
        var right = left + button.ActualWidth;
        if (left == _pillToLeft && right == _pillToRight) return;
        var first = double.IsNaN(_pillToLeft);
        _pillFromLeft = _pillLeft; _pillFromRight = _pillRight; _pillToLeft = left; _pillToRight = right;
        CompositionTarget.Rendering -= DrawPill;
        if (first || !animate || !_settings.AnimationsEnabled || !SystemParameters.ClientAreaAnimation)
        { _pillLeft = left; _pillRight = right; PlacePill(); return; }
        _pillClock.Restart();
        CompositionTarget.Rendering += DrawPill;
    }

    private void DrawPill(object? sender, EventArgs e)
    {
        var time = _pillClock.Elapsed.TotalMilliseconds * _settings.AnimationSpeed;
        // The leading edge arrives first and the other whips after it.
        var lead = Motion.Open(time / (Motion.OpenTime * .6)); var trail = Motion.Open(time / Motion.OpenTime);
        var forward = _pillToLeft > _pillFromLeft;
        _pillLeft = _pillFromLeft + (_pillToLeft - _pillFromLeft) * (forward ? trail : lead);
        _pillRight = _pillFromRight + (_pillToRight - _pillFromRight) * (forward ? lead : trail);
        if (time >= Motion.OpenTime) CompositionTarget.Rendering -= DrawPill;
        PlacePill();
    }

    private void PlacePill()
    {
        _pill.Margin = new Thickness(_pillLeft, 4, 0, 4);
        _pill.Width = Math.Max(0, _pillRight - _pillLeft);
    }

    private IEnumerable<View> Of(BarModuleKind kind) => _views.Where(view => view.Module.Kind == kind);

    private void Set(BarModuleKind kind, bool visible, string text, string label = "")
    {
        foreach (var view in Of(kind))
        {
            view.Frame.Visibility = Show(visible);
            var value = view.Icon != null || label.Length == 0 || view.Module.Format.Length > 0 ? text : label + " " + text;
            view.Text!.Text = view.Module.Format.Length == 0 ? value : view.Module.Format.Replace("{icon}", "").Replace("{value}", value).Trim();
            if (view.Icon != null && view.Module.Format.Length > 0) view.Icon.Visibility = Show(view.Module.Format.Contains("{icon}"));
            view.Text.Visibility = Show(view.Text.Text.Length > 0);
            Hide(view);
        }
    }

    public void UpdateTitle(string title)
    {
        if (_titleText == title) return;
        _titleText = title;
        ShowTitle();
    }

    private void ShowTitle() { Set(BarModuleKind.Title, _titleText.Length > 0, _titleText); UpdateSections(); }

    public void UpdateClock(DateTime now)
    {
        var usual = _settings.Use24Hour ? "HH:mm" : "h:mm tt";
        // With no date module of its own, the clock shows the date too.
        if (!_settings.Shows(BarModuleKind.Date)) usual = "ddd d MMM   " + usual;
        Set(BarModuleKind.Clock, true, Format(now, _settings.ClockFormat, usual));
        Set(BarModuleKind.Date, true, Format(now, _settings.DateFormat, "ddd d MMM"));
        foreach (var view in Of(BarModuleKind.Island)) ((BarIsland)view.Content).UpdateClock(Format(now, _settings.ClockFormat, _settings.Use24Hour ? "HH:mm" : "h:mm tt"));
    }

    /// <summary>A module that its style hides stays hidden.</summary>
    private void Hide(View view) { if (Look(view.Module.Style, view.Element).Hidden == true) view.Frame.Visibility = Visibility.Collapsed; }

    public void UpdateScript(BarModule module, string text, string tip)
    {
        _scripts[module] = (text, tip);
        ShowScripts();
    }

    private void ShowScripts()
    {
        foreach (var view in Of(BarModuleKind.Script))
        {
            if (!_scripts.TryGetValue(view.Module, out var output)) continue;
            view.Frame.Visibility = Show(output.Text.Length > 0);
            view.Text!.Text = view.Module.Format.Length == 0 ? output.Text : view.Module.Format.Replace("{icon}", "").Replace("{value}", output.Text).Trim();
            view.Frame.ToolTip = output.Tip.Length > 0 ? output.Tip : null;
            Hide(view);
        }
        UpdateSections();
    }

    internal static string Format(DateTime now, string format, string usual)
    {
        // A single letter means something else to .NET unless it is marked as a custom format.
        try { if (format.Length > 0) return now.ToString(format.Length == 1 ? "%" + format : format, CultureInfo.CurrentCulture); }
        catch (FormatException) { }
        return now.ToString(usual, CultureInfo.CurrentCulture);
    }

    public void UpdateMedia(NowPlayingState? state) { _playing = state; ShowMedia(); }

    private void ShowMedia()
    {
        var playing = _playing is { HasSession: true } ? _playing : null;
        foreach (var view in Of(BarModuleKind.Island)) ((BarIsland)view.Content).UpdateMusic(_playing);
        foreach (var view in Of(BarModuleKind.Media))
        {
            view.Frame.Visibility = Show(playing != null);
            if (playing == null) continue;
            var mark = _settings.MediaStyle == BarMediaStyle.Controls ? "" : playing.IsPlaying ? "♪ " : "⏸ ";
            view.Text!.Text = mark + playing.Title + (string.IsNullOrWhiteSpace(playing.Artist) ? "" : " – " + playing.Artist);
            if (playing.Artwork == null) _cover.Background = _accent;
            else if ((_cover.Background as ImageBrush)?.ImageSource != playing.Artwork) _cover.Background = new ImageBrush(playing.Artwork) { Stretch = Stretch.UniformToFill };
            _cover.Opacity = playing.IsPlaying ? 1 : .6;
            Hide(view);
            if (_toggle != null) ((TextBlock)_toggle.Content).Text = playing.IsPlaying ? "\uE769" : "\uE768";
            foreach (var (button, enabled) in new[] { (_previous, playing.CanPrevious), (_toggle, playing.CanToggle), (_next, playing.CanNext) })
                if (button != null) { button.IsEnabled = enabled; button.Opacity = enabled ? 1 : .35; }
        }
        UpdateSections();
    }

    public void UpdateSystem(SystemSnapshot reading) { _reading = reading; ShowSystem(); }

    private void ShowSystem()
    {
        if (_reading is not { } reading) return;
        foreach (var view in Of(BarModuleKind.Island)) ((BarIsland)view.Content).UpdateVolume(reading.Volume, reading.Muted);
        Set(BarModuleKind.Cpu, true, $"{reading.Cpu}%", "CPU");
        Set(BarModuleKind.Memory, true, $"{reading.Memory}%", "RAM");
        Set(BarModuleKind.Disk, true, $"{reading.Disk}%", "Disk");
        Set(BarModuleKind.Network, true, $"↓ {SystemSnapshot.Speed(reading.Down)}  ↑ {SystemSnapshot.Speed(reading.Up)}");
        Set(BarModuleKind.Uptime, true, SystemSnapshot.Duration(reading.Uptime), "Up");
        Set(BarModuleKind.Volume, reading.Volume.HasValue, reading.Muted ? "Muted" : $"{reading.Volume}%", reading.Muted ? "" : "Vol");
        foreach (var view in Of(BarModuleKind.Volume)) if (view.Icon != null && view.Module.Icon.Length == 0) view.Icon.Text = reading.Muted ? "\uE74F" : "\uE767";
        Set(BarModuleKind.Battery, reading.Battery.HasValue, $"{reading.Battery}%", reading.Charging ? "⚡" : "Bat");
        foreach (var view in Of(BarModuleKind.Battery))
        {
            if (view.Icon != null && view.Module.Icon.Length == 0) view.Icon.Text = reading.Charging ? "\uE83E" : "\uE83F";
            if (!reading.Charging && reading.Battery <= 20) view.Text!.Foreground = Frozen(Color.FromRgb(208, 138, 134)); else view.Text!.ClearValue(TextBlock.ForegroundProperty);
        }
        UpdateSections();
    }

    public void UpdateWeather(WeatherReport? report) { _weather = report; ShowWeather(); }

    private void ShowWeather()
    {
        Set(BarModuleKind.Weather, _weather != null, _weather?.Degrees(_weather.Temperature) ?? "");
        foreach (var view in Of(BarModuleKind.Weather))
        {
            if (_weather == null) continue;
            if (view.Icon != null && view.Module.Icon.Length == 0) view.Icon.Text = WeatherSource.Symbol(_weather.Code, _weather.IsDay);
            view.Frame.ToolTip = $"{_weather.Place}: {WeatherSource.Describe(_weather.Code)}";
        }
        UpdateSections();
    }

    private void UpdateSections()
    {
        foreach (var view in Of(BarModuleKind.Workspaces)) view.Frame.Visibility = Show(_settings.WorkspacesEnabled);
        foreach (var (section, row) in new[] { (_left, _leftRow), (_center, _centerRow), (_right, _rightRow) })
            section.Visibility = Show(row.Children.Cast<UIElement>().Any(child => child.Visibility == Visibility.Visible));
    }

    internal static Brush InkFor(Color color)
        => .299 * color.R + .587 * color.G + .114 * color.B > 140 ? Brushes.Black : Brushes.White;

    private static Color Parse(string text, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(text); }
        catch (Exception ex) when (ex is FormatException or NullReferenceException) { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <param name="Picture">How the workspace looked when it was last left, or null.</param>
internal sealed record WorkspacePreview(ImageSource? Picture, IReadOnlyList<string> Titles);
