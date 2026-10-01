using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using MenuItem = System.Windows.Controls.MenuItem;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;

namespace WallpaperProfiles.Engine;

internal static class DesktopCanvasProcess
{
    public static string DefaultFile => Path.Combine(AppPaths.BaseDir, "desktop-canvas.json");
    public static string MutexName(string file) => @"Local\Belie.Canvas." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..16];
    public static bool IsRunning(string file)
    {
        if (!Mutex.TryOpenExisting(MutexName(file), out var mutex)) return false;
        mutex.Dispose();
        return true;
    }
    public static void Start(string file)
    {
        var document = new DesktopCanvasStore(file).Load();
        if (!document.Enabled || !document.Widgets.Any(w => w.Enabled) || IsRunning(file)) return;
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Belie's executable was not found.");
        var canvasFile = Path.GetFullPath(file);
        using var currentProcess = Process.GetCurrentProcess();
        if (!IsProcessInJob(currentProcess.Handle, IntPtr.Zero, out var inJob))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (inJob)
        {
            // The WMI process provider starts an independent process outside the app's lifetime job.
            using var processClass = new ManagementClass("Win32_Process");
            using var arguments = processClass.GetMethodParameters("Create");
            arguments["CommandLine"] = $"\"{exe}\" --widget-host --canvas-file \"{canvasFile}\"";
            arguments["CurrentDirectory"] = Path.GetDirectoryName(exe);
            using var result = processClass.InvokeMethod("Create", arguments, null);
            if (result == null || Convert.ToUInt32(result["ReturnValue"]) != 0)
                throw new InvalidOperationException("The independent desktop widget host could not start.");
            Logger.Info("Desktop widgets started independently of the tray app.");
            return;
        }
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--widget-host");
        info.ArgumentList.Add("--canvas-file");
        info.ArgumentList.Add(canvasFile);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The desktop widgets could not start.");
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    public static void Stop(string file)
    {
        if (!EventWaitHandle.TryOpenExisting(MutexName(file) + ".Stop", out var stop)) return;
        using (stop) stop.Set();
    }
}

internal sealed class DesktopCanvasHost : IDisposable
{
    private readonly DesktopCanvasStore _store;
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _stop;
    private readonly EventWaitHandle _changed;
    private readonly RegisteredWaitHandle _changeWait;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, DesktopWidgetWindow> _windows = new();
    private readonly Func<DesktopWidget, DesktopWidgetWindow> _createWindow;
    private bool _disposed;
    private readonly HashSet<Guid> _linksRefreshing = new();
    private readonly Dictionary<Guid, DateTimeOffset> _linkAttempts = new();
    private readonly Dictionary<Guid, string> _linkSources = new();

    public DesktopCanvasHost(string file, Func<DesktopWidget, DesktopWidgetWindow>? createWindow = null)
    {
        _store = new DesktopCanvasStore(file);
        _mutex = new Mutex(true, DesktopCanvasProcess.MutexName(file), out var first);
        if (!first) { _mutex.Dispose(); throw new InvalidOperationException("The desktop canvas is already running."); }
        _stop = new EventWaitHandle(false, EventResetMode.AutoReset, DesktopCanvasProcess.MutexName(file) + ".Stop");
        _createWindow = createWindow ?? (widget => new DesktopWidgetWindow(widget, _store));
        _changed = new EventWaitHandle(false, EventResetMode.AutoReset, DesktopCanvasProcess.MutexName(file) + ".Changed");
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        _changeWait = ThreadPool.RegisterWaitForSingleObject(_changed, (_, _) =>
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(Refresh);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        System.Windows.Application.Current.Dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            if (_stop.WaitOne(0)) { System.Windows.Application.Current.Shutdown(); return; }
            var document = _store.Load();
            if (!document.Enabled || !document.Widgets.Any(w => w.Enabled))
            { System.Windows.Application.Current.Shutdown(); return; }
            var enabled = document.Widgets.Where(w => w.Enabled).ToDictionary(w => w.Id);
            foreach (var id in _windows.Keys.Where(id => !enabled.ContainsKey(id)).ToArray())
            {
                var window = _windows[id];
                _windows.Remove(id);
                window.Close();
            }
            foreach (var widget in enabled.Values)
            {
                if (!_windows.TryGetValue(widget.Id, out var window))
                {
                    window = _createWindow(widget);
                    _windows.Add(widget.Id, window);
                    window.Closed += (_, _) => _windows.Remove(widget.Id);
                    window.Show();
                }
                else window.Update(widget);
                window.RefreshCountdown();
                window.EnsureDesktop();
                if (widget.Kind == DesktopWidgetKind.Link && !_linksRefreshing.Contains(widget.Id)
                    && (!_linkSources.TryGetValue(widget.Id, out var source) || source != widget.Content + "|" + widget.LeetCodeUsername
                        || !_linkAttempts.TryGetValue(widget.Id, out var attempted) || DateTimeOffset.UtcNow - attempted > TimeSpan.FromMinutes(5))
                    && (!widget.LinkUpdatedAt.HasValue || DateTimeOffset.UtcNow - widget.LinkUpdatedAt > TimeSpan.FromMinutes(15)
                        || widget.IsLeetCode && widget.PotdDate != DateOnly.FromDateTime(DateTime.UtcNow)))
                {
                    _linksRefreshing.Add(widget.Id); _linkAttempts[widget.Id] = DateTimeOffset.UtcNow;
                    _linkSources[widget.Id] = widget.Content + "|" + widget.LeetCodeUsername;
                    RefreshLink(widget);
                }
            }
        }
        catch (Exception ex) { Logger.Error("Refreshing desktop widgets failed; the saved canvas was preserved.", ex); }
    }

    private async void RefreshLink(DesktopWidget widget)
    {
        try { await Task.Run(() => DesktopLinkService.RefreshAsync(_store, widget)); }
        catch (Exception ex) { Logger.Error("Refreshing link card failed.", ex); }
        finally { _linksRefreshing.Remove(widget.Id); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _changeWait.Unregister(null);
        _changed.Dispose();
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}

internal sealed class DesktopWidgetWindow : Window
{
    private DesktopWidget _widget;
    private readonly DesktopCanvasStore _store;
    private readonly TextBlock _title = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ContentControl _body = new();
    private readonly TextBlock _countdown = new() { FontSize = 30, FontWeight = FontWeights.Light, TextWrapping = TextWrapping.Wrap };
    private readonly Border _card = new();
    private readonly Grid _grid = new();
    private readonly Border _header = new();
    private readonly Grid _glass = new() { IsHitTestVisible = false };
    private readonly Border _frost = new() { Margin = new Thickness(-24), Effect = new BlurEffect { Radius = 24, RenderingBias = RenderingBias.Performance } };
    private readonly Border _glassTint = new();
    private readonly Border _reflection = new();
    private static BitmapSource? _wallpaper;
    private static string _wallpaperKey = "";
    private static string _wallpaperStyle = "10";
    private static DateTime _wallpaperChecked;
    private string _glassPosition = "";
    private readonly Thumb _resize = new() { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 8),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Opacity = .5 };
    private IntPtr _handle;
    private System.Drawing.Point? _dragStart;
    private double _startX, _startY;
    private bool _resizing;
    private readonly Func<System.Drawing.Point> _cursorPosition;
    private System.Drawing.Point _resizeStart;
    private double _resizeStartWidth, _resizeStartHeight;
    private Matrix _resizeScale;
    private double _resizeWidth, _resizeHeight;
    private bool _resizeDirty;
    private string _appearance = "";
    private Guid _contentWidget;
    private DesktopSketch? _sketch;
    private bool HasHeader => _widget.ShowHeader && !string.IsNullOrWhiteSpace(_widget.Title);

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    public DesktopWidgetWindow(DesktopWidget widget, DesktopCanvasStore store, Func<System.Drawing.Point>? cursorPosition = null)
    {
        _widget = widget;
        _store = store;
        _cursorPosition = cursorPosition ?? (() => System.Windows.Forms.Cursor.Position);
        Title = "Belie widget · " + widget.DisplayTitle;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Foreground = new SolidColorBrush(Color.FromRgb(241, 242, 238));
        FontFamily = new FontFamily("Segoe UI");
        Width = widget.Width; Height = widget.Height;
        var grid = _grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        grid.RowDefinitions.Add(new RowDefinition());
        var header = _header;
        header.Padding = new Thickness(16, 0, 16, 0); header.Background = Brushes.Transparent; header.Cursor = Cursors.SizeAll;
        _title.VerticalAlignment = VerticalAlignment.Center;
        header.Child = _title;
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (_widget.Locked) return;
            _dragStart = System.Windows.Forms.Cursor.Position;
            _startX = _widget.X; _startY = _widget.Y;
            header.CaptureMouse(); e.Handled = true;
        };
        _card.MouseMove += (_, _) =>
        {
            if (HasHeader || _dragStart is not { } start) return;
            var cursor = System.Windows.Forms.Cursor.Position;
            _widget.X = _startX + cursor.X - start.X; _widget.Y = _startY + cursor.Y - start.Y; Place();
        };
        _card.MouseLeftButtonUp += (_, _) =>
        {
            if (HasHeader || _dragStart == null) return;
            _dragStart = null; _card.ReleaseMouseCapture(); SaveLayout();
        };
        _card.LostMouseCapture += (_, _) =>
        {
            if (HasHeader || _dragStart == null) return;
            _dragStart = null; SaveLayout();
        };
        header.MouseMove += (_, _) =>
        {
            if (_dragStart is not { } start) return;
            var cursor = System.Windows.Forms.Cursor.Position;
            _widget.X = _startX + cursor.X - start.X;
            _widget.Y = _startY + cursor.Y - start.Y;
            Place();
        };
        header.MouseLeftButtonUp += (_, _) => { if (_dragStart == null) return; _dragStart = null; header.ReleaseMouseCapture(); SaveLayout(); };
        header.LostMouseCapture += (_, _) => { if (_dragStart == null) return; _dragStart = null; SaveLayout(); };
        grid.Children.Add(header);
        _body.Margin = new Thickness(16, 4, 16, 18);
        Grid.SetRow(_body, 1); grid.Children.Add(_body);
        Grid.SetRowSpan(_resize, 2); grid.Children.Add(_resize);
        _resize.Template = ResizeTemplate();
        _resize.DragStarted += (_, _) =>
        {
            if (_widget.Locked) return;
            _resizing = true; _resizeWidth = Width; _resizeHeight = Height;
            _resizeStart = _cursorPosition();
            _resizeStartWidth = Width; _resizeStartHeight = Height;
            _resizeScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            _resizeDirty = false;
            CompositionTarget.Rendering += ResizeFrame;
        };
        _resize.DragDelta += (_, _) =>
        {
            if (!_resizing) return;
            // Thumb offsets depend on its moving layout; use screen displacement once per frame.
            var cursor = _cursorPosition();
            _resizeWidth = Math.Clamp(_resizeStartWidth + (cursor.X - _resizeStart.X) / _resizeScale.M11, 120, 1600);
            _resizeHeight = Math.Clamp(_resizeStartHeight + (cursor.Y - _resizeStart.Y) / _resizeScale.M22, 80, 1200);
            _resizeDirty = true;
        };
        _resize.DragCompleted += (_, _) =>
        {
            if (!_resizing) return;
            CompositionTarget.Rendering -= ResizeFrame;
            ResizeFrame(this, EventArgs.Empty);
            _resizing = false; SaveLayout();
        };
        Closed += (_, _) => CompositionTarget.Rendering -= ResizeFrame;
        var surface = new Grid();
        _glass.Children.Add(_frost); _glass.Children.Add(_glassTint); _glass.Children.Add(_reflection);
        surface.Children.Add(_glass); surface.Children.Add(grid);
        _card.Child = surface;
        _glass.SizeChanged += (_, _) => UpdateGlassClip();
        Content = _card;
        _card.MouseLeftButtonDown += (_, e) =>
        {
            if (HasHeader || _widget.Locked || _widget.Kind is DesktopWidgetKind.Link or DesktopWidgetKind.Sketch) return;
            _dragStart = System.Windows.Forms.Cursor.Position; _startX = _widget.X; _startY = _widget.Y;
            _card.CaptureMouse(); e.Handled = true;
        };
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Edit widgets in Belie" };
        open.Click += (_, _) => OpenBelie();
        var hide = new MenuItem { Header = "Hide this widget" };
        hide.Click += (_, _) =>
        {
            try
            {
                _store.Update(document =>
                {
                    var saved = document.Widgets.FirstOrDefault(w => w.Id == _widget.Id);
                    if (saved != null) saved.Enabled = false;
                });
                Close();
            }
            catch (Exception ex) { Logger.Error("Hiding widget failed.", ex); }
        };
        menu.Items.Add(open); menu.Items.Add(hide); ContextMenu = menu;
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true; };
        Drop += (_, e) =>
        {
            try
            {
                var widgets = DesktopCanvasFeatures.ReadDrop(e.Data); var cursor = System.Windows.Forms.Cursor.Position;
                DesktopCanvasFeatures.SaveDrop(_store, widgets, cursor.X, cursor.Y);
            }
            catch (Exception ex) { ToolTip = ex.Message; }
            e.Handled = true;
        };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var ex = NativeMethods.GetWindowLong(_handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_handle, NativeMethods.GWL_EXSTYLE,
                (ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~8);
        };
        Loaded += (_, _) => EnsureDesktop();
        Update(widget);
    }

    private static ControlTemplate ResizeTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var hitArea = new FrameworkElementFactory(typeof(Border));
        hitArea.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "◢");
        text.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        text.SetValue(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Right);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        hitArea.AppendChild(text);
        template.VisualTree = hitArea;
        return template;
    }

    private void ResizeFrame(object? sender, EventArgs e)
    {
        if (!_resizing || !_resizeDirty) return;
        _resizeDirty = false;
        Width = _resizeWidth; Height = _resizeHeight;
        _widget.Width = Width; _widget.Height = Height;
        Place();
        UpdateLayout();
    }

    public void Update(DesktopWidget widget)
    {
        if (_dragStart != null || _resizing) return;
        _widget = widget;
        Width = widget.Width; Height = widget.Height;
        _card.Opacity = widget.Opacity;
        FontFamily = new FontFamily(widget.FontFamily);
        Foreground = ColorBrush(widget.TextColor, Color.FromRgb(241, 242, 238));
        FontWeight = widget.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle = widget.Italic ? FontStyles.Italic : FontStyles.Normal;
        _card.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, FontFamily);
        _card.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Foreground);
        _card.SetValue(System.Windows.Documents.TextElement.FontWeightProperty, FontWeight);
        _card.SetValue(System.Windows.Documents.TextElement.FontStyleProperty, FontStyle);
        _card.CornerRadius = new CornerRadius(widget.CornerRadius);
        _card.BorderThickness = new Thickness(widget.ShowBorder ? widget.BorderWidth : 0);
        var glass = widget.GlassEffect && widget.ShowBackground;
        _glass.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
        var borderBrush = ColorBrush(widget.BorderColor, Color.FromRgb(115, 133, 121));
        borderBrush.Opacity = 140d / 255;
        var rim = widget.BorderColor == "#738579" ? Colors.White : borderBrush.Color;
        _card.BorderBrush = glass
            ? new LinearGradientBrush(new GradientStopCollection {
                new(Color.FromArgb(190, rim.R, rim.G, rim.B), 0), new(Color.FromArgb(65, rim.R, rim.G, rim.B), .45),
                new(Color.FromArgb(110, rim.R, rim.G, rim.B), 1) }, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1))
            : borderBrush;
        _card.Background = glass ? Brushes.Transparent
            : widget.ShowBackground ? ColorBrush(widget.BackgroundColor, Color.FromRgb(29, 32, 30)) : Brushes.Transparent;
        if (glass)
        {
            var tint = ColorBrush(widget.BackgroundColor, Color.FromRgb(24, 35, 51)).Color;
            _glassTint.Background = new LinearGradientBrush(new GradientStopCollection {
                new(Color.FromArgb(95, tint.R, tint.G, tint.B), 0), new(Color.FromArgb(130, tint.R, tint.G, tint.B), .6),
                new(Color.FromArgb(155, tint.R, tint.G, tint.B), 1) }, 90);
            _reflection.Background = new LinearGradientBrush(new GradientStopCollection {
                new(Color.FromArgb(85, 255, 255, 255), 0), new(Color.FromArgb(18, 255, 255, 255), .32),
                new(Colors.Transparent, .58), new(Color.FromArgb(12, 230, 241, 255), 1) },
                new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
            UpdateGlassClip();
            RefreshGlass();
        }
        _header.Visibility = HasHeader ? Visibility.Visible : Visibility.Collapsed;
        _grid.RowDefinitions[0].Height = new GridLength(HasHeader ? 38 : 0);
        _body.Margin = new Thickness(16, HasHeader ? 4 : 16, 16, 18);
        _resize.Visibility = widget.Locked ? Visibility.Collapsed : Visibility.Visible;
        _title.Text = widget.Title;
        Title = "Belie widget · " + widget.DisplayTitle;
        var appearance = widget.Kind + "\n" + widget.Content + "\n" + widget.FontSize + "\n" + widget.Alignment + "\n" + widget.ImageFit;
        if (widget.Kind == DesktopWidgetKind.Link)
            appearance += $"|{widget.LinkPageTitle}|{widget.LinkDescription}|{widget.LinkCustomDescription}|{widget.LeetCodeUsername}|{widget.LinkUpdatedAt}|{DateTime.UtcNow.Date}";
        if (widget.Kind == DesktopWidgetKind.Sketch)
            appearance = $"Sketch|{widget.Id}|{widget.Locked}|{widget.TextColor}";
        if (widget.Kind == DesktopWidgetKind.Image)
            appearance += "\n" + (File.Exists(widget.Content) ? File.GetLastWriteTimeUtc(widget.Content).Ticks.ToString() : "missing");
        if (_appearance != appearance || _contentWidget != widget.Id)
        {
            _appearance = appearance;
            _contentWidget = widget.Id;
            switch (widget.Kind)
            {
                case DesktopWidgetKind.Note:
                    _body.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new TextBlock { Text = widget.Content, FontSize = widget.FontSize,
                            TextAlignment = (System.Windows.TextAlignment)widget.Alignment, TextWrapping = TextWrapping.Wrap } };
                    break;
                case DesktopWidgetKind.Countdown:
                    _body.Content = _countdown;
                    break;
                case DesktopWidgetKind.Image:
                    var image = ThumbnailLoader.Load(widget.Content, 1200);
                    _body.Content = image == null
                        ? new TextBlock { Text = "Image unavailable\nChoose another file in Belie.", TextWrapping = TextWrapping.Wrap }
                        : new Image { Source = image, Stretch = widget.ImageFit switch { WidgetImageFit.Fill => Stretch.UniformToFill,
                            WidgetImageFit.Stretch => Stretch.Fill, _ => Stretch.Uniform } };
                    break;
                case DesktopWidgetKind.Link:
                    _body.Content = LinkCard(widget);
                    break;
                case DesktopWidgetKind.Sketch:
                    var sketchId = widget.Id;
                    _sketch = new DesktopSketch(widget, (drawing, width, height) =>
                    {
                        try { _store.UpdateWidget(sketchId, saved => { saved.Drawing = drawing; saved.DrawingWidth = width; saved.DrawingHeight = height; }); }
                        catch (Exception ex) { Logger.Error("Saving sketch failed.", ex); }
                    }, () => { _dragStart = System.Windows.Forms.Cursor.Position; _startX = _widget.X; _startY = _widget.Y; },
                    () =>
                    {
                        if (_dragStart is not { } start) return;
                        var cursor = System.Windows.Forms.Cursor.Position;
                        _widget.X = _startX + cursor.X - start.X; _widget.Y = _startY + cursor.Y - start.Y; Place();
                    }, () => { _dragStart = null; SaveLayout(); });
                    _body.Content = _sketch;
                    break;
            }
        }
        if (widget.Kind == DesktopWidgetKind.Sketch) _sketch?.UpdateDrawing(widget);
        RefreshCountdown();
        Place();
    }

    private FrameworkElement LinkCard(DesktopWidget widget)
    {
        var panel = new StackPanel();
        void Text(string text, double size, double opacity = 1)
            => panel.Children.Add(new TextBlock { Text = text, FontSize = size, Opacity = opacity, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 7) });
        if (DesktopWidget.TryGetLink(widget.Content, out var address))
        {
            Text(address!.Host.ToUpperInvariant(), 10, .65);
            if (widget.IsLeetCode)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                Text($"{widget.PotdStreak(today)} day verified POTD streak", widget.FontSize);
                Text($"{widget.ActivityStreak} day activity streak · {widget.LeetCodeUsername}", 12, .8);
                Text(string.IsNullOrWhiteSpace(widget.PotdTitle) ? "Problem of the day" : widget.PotdTitle, Math.Min(widget.FontSize, 18));
                Text(widget.PotdDate != today ? "Fetching today's challenge…" : widget.PotdCompletions.Contains(today) ? "Completed today ✓" : "Today's POTD is open", 12);
                Text("Public accepted submissions · UTC days · recent verified history", 10, .6);
            }
            else
            {
                var fallback = Uri.UnescapeDataString(address.AbsolutePath.Trim('/')).Replace('-', ' ').Replace('_', ' ');
                Text(string.IsNullOrWhiteSpace(widget.LinkPageTitle) ? (fallback.Length == 0 ? address.Host : fallback) : widget.LinkPageTitle, widget.FontSize);
                var description = string.IsNullOrWhiteSpace(widget.LinkCustomDescription) ? widget.LinkDescription : widget.LinkCustomDescription;
                Text(string.IsNullOrWhiteSpace(description) ? "Your shortcut to " + address.Host : description, 12, .8);
            }
            if (!string.IsNullOrWhiteSpace(widget.LinkStatus)) Text(widget.LinkStatus, 10, .65);
            if (widget.LinkUpdatedAt is { } updated) Text("Updated " + updated.LocalDateTime.ToString("MMM d · HH:mm"), 10, .55);
        }
                    var valid = DesktopWidget.TryGetLink(widget.Content, out var uri);
                    var button = new Button { Content = valid ? widget.IsLeetCode ? "Open today's challenge ↗" : "Open ↗" : "Link unavailable", IsEnabled = valid,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
                        Padding = new Thickness(12), ToolTip = widget.Content, FontSize = widget.FontSize };
                    button.Click += (_, _) =>
                    {
                        try { if (DesktopWidget.TryGetLink(_widget.IsLeetCode && !string.IsNullOrWhiteSpace(_widget.PotdUrl) ? _widget.PotdUrl : _widget.Content, out var link)) Process.Start(new ProcessStartInfo(link!.AbsoluteUri) { UseShellExecute = true }); }
                        catch (Exception ex) { Logger.Error("Opening widget link failed.", ex); }
                    };
        panel.Children.Add(button);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public void RefreshCountdown()
    {
        if (_widget.Kind == DesktopWidgetKind.Countdown)
        {
            _countdown.Text = _widget.CountdownText(DateTimeOffset.Now);
            _countdown.FontSize = _widget.FontSize;
            _countdown.FontWeight = _widget.Bold ? FontWeights.Bold : FontWeights.Light;
            _countdown.TextAlignment = (System.Windows.TextAlignment)_widget.Alignment;
        }
    }

    private static SolidColorBrush ColorBrush(string text, Color fallback)
        => new(SceneController.TryParseAccent(text, out var color) ? color : fallback);

    private void UpdateGlassClip()
    {
        _glass.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, _glass.ActualWidth), Math.Max(0, _glass.ActualHeight)),
            _widget.CornerRadius, _widget.CornerRadius);
    }

    private void RefreshGlass()
    {
        if (!_widget.GlassEffect || !_widget.ShowBackground) return;
        // Sample only the wallpaper file, so app windows and desktop content never become part of the material.
        if (DateTime.UtcNow - _wallpaperChecked > TimeSpan.FromSeconds(2))
        {
            _wallpaperChecked = DateTime.UtcNow;
            try
            {
                using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var file = desktop?.GetValue("WallPaper") as string ?? "";
                _wallpaperStyle = desktop?.GetValue("WallpaperStyle") as string ?? "10";
                var key = file + "|" + (File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0);
                if (key != _wallpaperKey)
                { _wallpaperKey = key; _wallpaper = ThumbnailLoader.Load(file, 1920) as BitmapSource; }
            }
            catch { _wallpaper = null; }
        }
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var bounds = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)_widget.X, (int)_widget.Y)).Bounds;
        var position = $"{_wallpaperKey}|{_wallpaperStyle}|{_widget.X}|{_widget.Y}|{Width}|{Height}|{scale.M11}|{bounds}";
        if (_glassPosition == position) return;
        _glassPosition = position;
        if (_wallpaper == null) { _frost.Background = Brushes.Transparent; return; }
        if (_wallpaperStyle == "22") bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var imageWidth = (double)_wallpaper.PixelWidth; var imageHeight = (double)_wallpaper.PixelHeight;
        var ratio = _wallpaperStyle == "6" ? Math.Min(bounds.Width / imageWidth, bounds.Height / imageHeight)
            : Math.Max(bounds.Width / imageWidth, bounds.Height / imageHeight);
        var renderedWidth = _wallpaperStyle == "2" ? bounds.Width : imageWidth * ratio;
        var renderedHeight = _wallpaperStyle == "2" ? bounds.Height : imageHeight * ratio;
        var imageX = bounds.Left + (bounds.Width - renderedWidth) / 2;
        var imageY = bounds.Top + (bounds.Height - renderedHeight) / 2;
        var brush = new ImageBrush(_wallpaper) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect((_widget.X - 24 * scale.M11 - imageX) / renderedWidth,
                (_widget.Y - 24 * scale.M22 - imageY) / renderedHeight,
                (Width + 48) * scale.M11 / renderedWidth, (Height + 48) * scale.M22 / renderedHeight) };
        _frost.Background = brush;
    }

    public static IntPtr FindDesktop()
    {
        var parent = IntPtr.Zero;
        NativeMethods.EnumWindows((window, _) =>
        {
            parent = NativeMethods.FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            return parent == IntPtr.Zero;
        }, IntPtr.Zero);
        return parent == IntPtr.Zero ? NativeMethods.GetShellWindow() : GetAncestor(parent, 2);
    }

    public void EnsureDesktop()
    {
        if (_handle == IntPtr.Zero || !IsWindow(_handle)) return;
        if (NativeMethods.IsIconic(_handle)) NativeMethods.ShowWindow(_handle, 4);
        Place();
    }

    private IntPtr DesktopZOrder()
    {
        if (Topmost) Topmost = false;
        var desktop = FindDesktop();
        if (desktop == IntPtr.Zero) return new IntPtr(-2);
        var previous = GetWindow(desktop, 3);
        if (previous == _handle) return _handle;
        if (previous != IntPtr.Zero && (NativeMethods.GetWindowLong(previous, NativeMethods.GWL_EXSTYLE) & 8) != 0)
            return new IntPtr(-2);
        return previous;
    }

    private void Place()
    {
        if (_handle == IntPtr.Zero || _dragStart != null && !IsMouseCaptured && !IsMouseCaptureWithin) return;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)_widget.X, (int)_widget.Y));
        var work = screen.WorkingArea;
        var width = (int)Math.Ceiling(Width * scale.M11);
        var height = (int)Math.Ceiling(Height * scale.M22);
        var x = Math.Clamp((int)_widget.X, work.Left, Math.Max(work.Left, work.Right - width));
        var y = Math.Clamp((int)_widget.Y, work.Top, Math.Max(work.Top, work.Bottom - height));
        _widget.X = x; _widget.Y = y;
        RefreshGlass();
        if (Topmost) Topmost = false;
        var order = DesktopZOrder();
        NativeMethods.SetWindowPos(_handle, order, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | (order == _handle ? NativeMethods.SWP_NOZORDER : 0));
    }

    private void SaveLayout()
    {
        try { _store.UpdateLayout(_widget.Id, _widget.X, _widget.Y, Width, Height); }
        catch (Exception ex) { Logger.Error("Saving widget layout failed.", ex); }
    }

    private static void OpenBelie()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(@"Local\Belie.Canvas.Open", out var activate))
            { using (activate) activate.Set(); return; }
            var exe = Environment.ProcessPath;
            if (exe != null) Process.Start(new ProcessStartInfo(exe, "--desktop-canvas") { UseShellExecute = false });
        }
        catch (Exception ex) { Logger.Error("Opening Belie from a widget failed.", ex); }
    }
}
