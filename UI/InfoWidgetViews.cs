using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace WallpaperProfiles.UI;

internal static class WidgetAlignment
{
    /// <summary>WPF lists its alignments in another order, so a cast would swap center and right.</summary>
    public static TextAlignment ToText(this WidgetTextAlignment alignment) => alignment switch
    { WidgetTextAlignment.Center => TextAlignment.Center, WidgetTextAlignment.Right => TextAlignment.Right, _ => TextAlignment.Left };
}

/// <summary>Shared by the widgets that show information: their colors, and a clock that only runs while they can be seen.</summary>
internal abstract class InfoWidgetView : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _timer;
    protected DesktopWidget Widget { get; private set; }
    protected Brush Ink { get; private set; } = Brushes.White;
    protected Brush Accent { get; private set; } = Brushes.White;
    /// <summary>Shows sample values, for the gallery.</summary>
    public bool PreviewMode { get; init; }

    protected InfoWidgetView(DesktopWidget widget, TimeSpan interval)
    {
        Widget = widget;
        Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)); // So it can be dragged anywhere.
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => Run();
        Unloaded += (_, _) => { _timer.Stop(); Stopped(); };
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) Run(); else { _timer.Stop(); Stopped(); } };
    }

    private void Run() { if (!IsVisible) return; Refresh(); if (!PreviewMode) _timer.Start(); }

    internal void UpdateAppearance(DesktopWidget widget)
    {
        Widget = widget;
        Ink = Paint(widget.TextColor, Colors.White);
        Accent = Paint(widget.MusicAccentColor, Color.FromRgb(169, 201, 180));
        Foreground = Ink; FontFamily = new FontFamily(widget.FontFamily); FontSize = widget.FontSize;
        FontWeight = widget.Bold ? FontWeights.Bold : FontWeights.Normal; FontStyle = widget.Italic ? FontStyles.Italic : FontStyles.Normal;
        AppearanceChanged();
        Refresh();
    }

    protected virtual void AppearanceChanged() { }
    protected virtual void Stopped() { }
    protected abstract void Refresh();

    protected static Brush Paint(string value, Color fallback)
    {
        var brush = new SolidColorBrush(SceneController.TryParseAccent(value, out var color) ? color : fallback);
        brush.Freeze();
        return brush;
    }

    protected FormattedText Words(string text, double size, Brush? brush = null, bool strong = false) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyle, strong ? FontWeights.SemiBold : FontWeight, FontStretches.Normal), Math.Max(6, size), brush ?? Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected static Button Glyph(string glyph, string label, double size, Action click)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(size));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var button = new Button
        {
            Style = new Style(typeof(Button)), Template = new ControlTemplate(typeof(Button)) { VisualTree = border }, Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = size, Width = size * 2, Height = size * 2, Padding = new Thickness(0), Cursor = Cursors.Hand, ToolTip = label, FontWeight = FontWeights.Normal, Focusable = true
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }

    internal static void DrawArc(DrawingContext drawing, Pen pen, Point center, double radius, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        if (amount <= 0 || radius <= 0) return;
        if (amount >= .9999) { drawing.DrawEllipse(null, pen, center, radius, radius); return; }
        var angle = amount * Math.PI * 2 - Math.PI / 2;
        var arc = new StreamGeometry();
        using (var path = arc.Open())
        {
            path.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            path.ArcTo(new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius), new Size(radius, radius), 0, amount > .5, SweepDirection.Clockwise, true, false);
        }
        drawing.DrawGeometry(null, pen, arc);
    }
}

/// <summary>Processor, memory, disk, network, battery and uptime.</summary>
internal sealed class SystemWidgetView : InfoWidgetView
{
    private sealed record Reading(string Name, string Value, double? Amount, List<double> Past);
    private readonly Dictionary<string, List<double>> _past = new();
    private readonly Surface _surface;
    private SystemReadings? _readings;
    private List<Reading> _shown = new();

    public SystemWidgetView(DesktopWidget widget) : base(widget, TimeSpan.FromSeconds(2))
    {
        Content = _surface = new Surface(this);
        AutomationProperties.SetName(this, "System monitor");
        UpdateAppearance(widget);
    }

    protected override void Stopped() { _readings?.Dispose(); _readings = null; }

    protected override void Refresh()
    {
        var metrics = Widget.SystemMetrics;
        if (!PreviewMode && !IsVisible) return;
        var reading = PreviewMode ? new SystemSnapshot(34, 58, 40, false, 82, false, 47, 2_400_000, 310_000, TimeSpan.FromHours(5.3))
            : (_readings ??= new SystemReadings()).Read(metrics.HasFlag(SystemMetrics.Disk), metrics.HasFlag(SystemMetrics.Network));
        var shown = new List<Reading>();
        void Add(SystemMetrics metric, string name, string value, double? amount)
        {
            if (!metrics.HasFlag(metric)) return;
            if (!_past.TryGetValue(name, out var past)) _past[name] = past = new List<double>();
            if (PreviewMode && past.Count == 0) for (var i = 0; i < 40; i++) past.Add((amount ?? .3) * (.65 + .35 * Math.Sin(i * .45 + name.Length)));
            else if (amount is { } level) past.Add(level);
            if (past.Count > 60) past.RemoveAt(0);
            shown.Add(new Reading(name, value, amount, past));
        }
        Add(SystemMetrics.Cpu, "CPU", reading.Cpu + "%", reading.Cpu / 100d);
        Add(SystemMetrics.Memory, "Memory", reading.Memory + "%", reading.Memory / 100d);
        Add(SystemMetrics.Disk, "Disk", reading.Disk + "%", reading.Disk / 100d);
        // There is no fixed top speed, so the scale is a soft one that 10 MB/s nearly fills.
        Add(SystemMetrics.Network, "Network", "↓ " + SystemSnapshot.Speed(reading.Down), 1 - Math.Exp(-(reading.Down + reading.Up) / 4_000_000));
        if (reading.Battery is { } battery) Add(SystemMetrics.Battery, "Battery", battery + "%" + (reading.Charging ? " ⚡" : ""), battery / 100d);
        Add(SystemMetrics.Uptime, "Uptime", SystemSnapshot.Duration(reading.Uptime), null);
        _shown = shown;
        _surface.InvalidateVisual();
    }

    private sealed class Surface(SystemWidgetView owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing)
        {
            var shown = owner._shown;
            if (shown.Count == 0 || ActualWidth < 20 || ActualHeight < 20) return;
            switch (owner.Widget.SystemSkin)
            {
                case SystemSkin.Rings: Rings(drawing, shown); break;
                case SystemSkin.Graph: Rows(drawing, shown, graph: true); break;
                case SystemSkin.Compact: Compact(drawing, shown); break;
                default: Rows(drawing, shown, graph: false); break;
            }
        }

        private void Rings(DrawingContext drawing, List<Reading> shown)
        {
            // The grid that leaves the largest rings.
            var columns = Enumerable.Range(1, shown.Count).OrderByDescending(count =>
                Math.Min(ActualWidth / count, ActualHeight / Math.Ceiling(shown.Count / (double)count))).First();
            var rows = (int)Math.Ceiling(shown.Count / (double)columns);
            double width = ActualWidth / columns, height = ActualHeight / rows;
            for (var i = 0; i < shown.Count; i++)
            {
                var label = owner.Words(shown[i].Name, Math.Min(owner.FontSize * .8, height / 8));
                var radius = Math.Max(4, Math.Min(width, height - label.Height - 4) / 2 - 8);
                var center = new Point(width * (i % columns) + width / 2, height * (i / columns) + radius + 8);
                var thickness = Math.Max(3, radius * .16);
                var track = new Pen(Faded(owner.Ink, .16), thickness);
                drawing.DrawEllipse(null, track, center, radius - thickness / 2, radius - thickness / 2);
                DrawArc(drawing, new Pen(owner.Accent, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, center, radius - thickness / 2, shown[i].Amount ?? 1);
                var value = owner.Words(shown[i].Value.Replace("↓ ", "").Replace(" ", "\n"), shown[i].Value.Length > 5 ? radius * .3 : radius * .46, strong: true);
                value.TextAlignment = TextAlignment.Center; value.MaxTextWidth = Math.Max(10, radius * 2 - thickness * 2);
                drawing.DrawText(value, new Point(center.X - value.MaxTextWidth / 2, center.Y - value.Height / 2));
                drawing.DrawText(label, new Point(center.X - label.Width / 2, center.Y + radius + 3));
            }
        }

        private void Rows(DrawingContext drawing, List<Reading> shown, bool graph)
        {
            var height = ActualHeight / shown.Count;
            var size = Math.Min(owner.FontSize, height * (graph ? .28 : .38));
            for (var i = 0; i < shown.Count; i++)
            {
                var top = i * height;
                var label = owner.Words(shown[i].Name, size); var value = owner.Words(shown[i].Value, size, strong: true);
                drawing.DrawText(label, new Point(0, top)); drawing.DrawText(value, new Point(Math.Max(0, ActualWidth - value.Width), top));
                var start = top + label.Height + 4;
                var space = Math.Max(2, height - label.Height - 12);
                if (shown[i].Amount is not { } amount) continue;
                if (!graph)
                {
                    var thick = Math.Min(space, Math.Max(4, size * .5));
                    drawing.DrawRoundedRectangle(Faded(owner.Ink, .16), null, new Rect(0, start, ActualWidth, thick), thick / 2, thick / 2);
                    if (amount > 0) drawing.DrawRoundedRectangle(owner.Accent, null, new Rect(0, start, Math.Max(thick, ActualWidth * Math.Min(1, amount)), thick), thick / 2, thick / 2);
                    continue;
                }
                var past = shown[i].Past;
                if (past.Count < 2) continue;
                Point At(int index) => new(ActualWidth - (past.Count - 1 - index) * ActualWidth / 59, start + space * (1 - Math.Clamp(past[index], 0, 1)));
                foreach (var fill in new[] { true, false })
                {
                    var line = new StreamGeometry();
                    using (var path = line.Open())
                    {
                        path.BeginFigure(fill ? new Point(At(0).X, start + space) : At(0), fill, fill);
                        for (var point = fill ? 0 : 1; point < past.Count; point++) path.LineTo(At(point), true, true);
                        if (fill) path.LineTo(new Point(ActualWidth, start + space), true, false);
                    }
                    drawing.DrawGeometry(fill ? Faded(owner.Accent, .18) : null, fill ? null : new Pen(owner.Accent, 2) { LineJoin = PenLineJoin.Round }, line);
                }
            }
        }

        private void Compact(DrawingContext drawing, List<Reading> shown)
        {
            var size = Math.Min(owner.FontSize, ActualHeight / shown.Count / 1.5);
            double y = 0;
            foreach (var reading in shown)
            {
                var label = owner.Words(reading.Name.ToLowerInvariant(), size, owner.Accent); var value = owner.Words(reading.Value, size, strong: true);
                drawing.DrawText(label, new Point(0, y)); drawing.DrawText(value, new Point(Math.Max(size * 5.5, label.Width + 10), y));
                y += label.Height * 1.15;
            }
        }

        private static Brush Faded(Brush brush, double opacity)
        {
            var faded = brush.Clone(); faded.Opacity = opacity; faded.Freeze();
            return faded;
        }
    }
}

internal sealed class WeatherWidgetView : InfoWidgetView
{
    private static readonly WeatherReport Sample = new("Your city", 21, 20, 56, 12, 2, true, false, Enumerable.Range(0, 5)
        .Select(day => new WeatherDay(DateOnly.FromDateTime(DateTime.Today).AddDays(day), new[] { 2, 0, 61, 3, 0 }[day], 23 + day % 3, 14 + day % 2)).ToArray());
    private readonly Grid _root = new();
    private readonly DispatcherTimer _typing = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private WeatherReport? _report;
    private bool _reading;
    private string _shown = "", _place = "";

    public WeatherWidgetView(DesktopWidget widget) : base(widget, TimeSpan.FromMinutes(1))
    {
        Content = new Viewbox { Child = _root, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(this, "Weather");
        _typing.Tick += (_, _) => { _typing.Stop(); Refresh(); };
        _place = widget.Content;
        UpdateAppearance(widget);
    }

    protected override void AppearanceChanged()
    {
        _shown = "";
        if (_place == Widget.Content) return;
        // Only a finished name is looked up, not every letter on the way.
        _place = Widget.Content; _report = null;
        _typing.Stop(); _typing.Start();
    }

    protected override void Stopped() => _typing.Stop();

    protected override async void Refresh()
    {
        if (PreviewMode) { _report = Sample; Show(); return; }
        Show();
        if (_reading || _typing.IsEnabled || !IsVisible) return;
        _reading = true;
        try { _report = await WeatherSource.GetAsync(Widget.Content, Widget.WeatherFahrenheit); Show(); }
        catch (Exception ex) { Infrastructure.Logger.Error("Showing the weather failed.", ex); }
        finally { _reading = false; }
    }

    private void Show()
    {
        var key = $"{Widget.WeatherSkin}|{Widget.Content}|{_report?.GetHashCode()}";
        if (_shown == key) return;
        _shown = key;
        _root.Children.Clear();
        TextBlock Text(string text, double size, double opacity = 1, Brush? brush = null, string? font = null) => new()
        {
            Text = text, FontSize = size, Opacity = opacity, Foreground = brush ?? Ink, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = font == null ? FontFamily : new FontFamily(font)
        };
        if (_report is not { } report)
        {
            _root.Children.Add(Text(string.IsNullOrWhiteSpace(Widget.Content) ? "Type a place in Widgets" : "Weather unavailable", FontSize));
            return;
        }
        TextBlock Symbol(int code, double size, bool day = true) => Text(WeatherSource.Symbol(code, day), size, 1, Accent, "Segoe UI Symbol");
        var size = FontSize;
        var now = new StackPanel { Orientation = Orientation.Horizontal };
        now.Children.Add(Symbol(report.Code, size * 3, report.IsDay));
        var temperature = Text(report.Degrees(report.Temperature), size * 2.6); temperature.Margin = new Thickness(size * .7, 0, size * .9, 0);
        now.Children.Add(temperature);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Text(WeatherSource.Describe(report.Code), size));
        words.Children.Add(Text(report.Place, size * .8, .7));
        now.Children.Add(words);
        var panel = new StackPanel();
        panel.Children.Add(now);
        if (Widget.WeatherSkin != WeatherSkin.Compact)
        {
            var details = Text($"Feels like {report.Degrees(report.FeelsLike)}   ·   {report.Humidity}% humidity   ·   {report.WindText}", size * .75, .75);
            details.Margin = new Thickness(0, size * .6, 0, 0);
            panel.Children.Add(details);
        }
        if (Widget.WeatherSkin == WeatherSkin.Forecast)
        {
            var days = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, size * .9, 0, 0) };
            foreach (var day in report.Days)
            {
                var column = new StackPanel { Margin = new Thickness(0, 0, size * .9, 0) };
                column.Children.Add(Text(day.Date.ToString("ddd", CultureInfo.CurrentCulture), size * .75, .7));
                column.Children.Add(Symbol(day.Code, size * 1.4));
                column.Children.Add(Text(report.Degrees(day.High), size * .9));
                column.Children.Add(Text(report.Degrees(day.Low), size * .75, .6));
                foreach (TextBlock line in column.Children) line.HorizontalAlignment = HorizontalAlignment.Center;
                days.Children.Add(column);
            }
            panel.Children.Add(days);
        }
        _root.Children.Add(panel);
    }
}

internal sealed class CalendarWidgetView : InfoWidgetView
{
    private readonly Grid _days = new();
    private readonly TextBlock _month = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly Button _previous, _next;
    private DateTime _shownMonth = DateTime.Today;
    private string _shown = "";
    private DateTime _today = DateTime.Today;

    public CalendarWidgetView(DesktopWidget widget) : base(widget, TimeSpan.FromMinutes(1))
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _previous = Glyph("", "Previous month", 10, () => Move(-1)); _next = Glyph("", "Next month", 10, () => Move(1));
        DockPanel.SetDock(_next, Dock.Right); DockPanel.SetDock(_previous, Dock.Right);
        header.Children.Add(_next); header.Children.Add(_previous); header.Children.Add(_month);
        var root = new DockPanel(); DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header); root.Children.Add(_days);
        Content = root;
        for (var i = 0; i < 7; i++) { _days.ColumnDefinitions.Add(new ColumnDefinition()); _days.RowDefinitions.Add(new RowDefinition()); }
        AutomationProperties.SetName(this, "Calendar");
        UpdateAppearance(widget);
    }

    private void Move(int months) { _shownMonth = _shownMonth.AddMonths(months); Refresh(); }

    protected override void AppearanceChanged() => _shown = "";

    protected override void Refresh()
    {
        if (_today != DateTime.Today) { _today = DateTime.Today; _shownMonth = _today; }
        var key = $"{_shownMonth:yyyyMM}|{_today:yyyyMMdd}";
        if (_shown == key) return;
        _shown = key;
        _previous.Foreground = _next.Foreground = Ink; _month.Foreground = Ink; _month.FontSize = FontSize * 1.1;
        _month.Text = _shownMonth.ToString("Y", CultureInfo.CurrentCulture);
        _days.Children.Clear();
        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        var first = new DateTime(_shownMonth.Year, _shownMonth.Month, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek - (int)format.FirstDayOfWeek + 7) % 7));
        for (var column = 0; column < 7; column++)
            Add(format.AbbreviatedDayNames[((int)format.FirstDayOfWeek + column) % 7], 0, column, .6, false);
        for (var cell = 0; cell < 42; cell++)
        {
            var day = start.AddDays(cell);
            Add(day.Day.ToString(CultureInfo.CurrentCulture), 1 + cell / 7, cell % 7, day.Month == _shownMonth.Month ? 1 : .28, day == _today);
        }
    }

    private void Add(string text, int row, int column, double opacity, bool today)
    {
        var label = new TextBlock { Text = text, FontSize = FontSize * (row == 0 ? .75 : .9), Foreground = today ? TopBarWindow.InkFor(((SolidColorBrush)Accent).Color) : Ink,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = today ? FontWeights.SemiBold : FontWeight };
        var cell = new Border { Child = label, Opacity = opacity, Background = today ? Accent : Brushes.Transparent, CornerRadius = new CornerRadius(8), Margin = new Thickness(1) };
        Grid.SetRow(cell, row); Grid.SetColumn(cell, column); _days.Children.Add(cell);
    }
}

/// <summary>A list to tick off. Each line of the widget's content is an item; done ones start with "[x] ".</summary>
internal sealed class ChecklistWidgetView : InfoWidgetView
{
    private const string Done = "[x] ";
    private readonly StackPanel _items = new();
    private readonly Action<string> _save;
    private string _shown = "";

    public ChecklistWidgetView(DesktopWidget widget, Action<string>? save = null) : base(widget, TimeSpan.FromHours(1))
    {
        _save = save ?? (_ => { });
        Content = new ScrollViewer { Content = _items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(this, "Checklist");
        UpdateAppearance(widget);
    }

    protected override void AppearanceChanged() => _shown = "";

    internal static string Toggle(string content, int index)
    {
        var lines = Lines(content);
        if (index < 0 || index >= lines.Length) return content;
        lines[index] = lines[index].StartsWith(Done, StringComparison.OrdinalIgnoreCase) ? lines[index][Done.Length..] : Done + lines[index];
        return string.Join("\n", lines);
    }

    private static string[] Lines(string content) => content.Replace("\r", "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();

    protected override void Refresh()
    {
        if (_shown == Widget.Content && _items.Children.Count > 0) return;
        _shown = Widget.Content;
        _items.Children.Clear();
        var lines = Lines(Widget.Content);
        if (lines.Length == 0) _items.Children.Add(new TextBlock { Text = "Add items in Widgets, one per line.", TextWrapping = TextWrapping.Wrap, Foreground = Ink, Opacity = .7, FontSize = FontSize });
        for (var i = 0; i < lines.Length; i++)
        {
            var index = i;
            var done = lines[i].StartsWith(Done, StringComparison.OrdinalIgnoreCase);
            var name = done ? lines[i][Done.Length..] : lines[i];
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, FontSize * .35), Background = Brushes.Transparent };
            var tick = Glyph(done ? "" : "", (done ? "Mark as not done: " : "Mark as done: ") + name, FontSize * .6, () =>
            {
                var content = Toggle(Widget.Content, index);
                Widget.Content = content; _save(content); Refresh();
            });
            tick.Foreground = done ? TopBarWindow.InkFor(((SolidColorBrush)Accent).Color) : Ink;
            var box = new Border { Child = tick, Width = FontSize * 1.3, Height = FontSize * 1.3, CornerRadius = new CornerRadius(FontSize * .3), BorderBrush = Accent, BorderThickness = new Thickness(1.5),
                Background = done ? Accent : Brushes.Transparent, Margin = new Thickness(0, 1, FontSize * .6, 0), VerticalAlignment = VerticalAlignment.Top };
            tick.Width = tick.Height = double.NaN;
            DockPanel.SetDock(box, Dock.Left); row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, FontSize = FontSize, Foreground = Ink, Opacity = done ? .5 : 1,
                TextDecorations = done ? TextDecorations.Strikethrough : null });
            _items.Children.Add(row);
        }
    }
}

/// <summary>Alternates focus and break time.</summary>
internal sealed class TimerWidgetView : InfoWidgetView
{
    private readonly Ring _ring;
    private readonly Button _toggle, _reset;
    private readonly TextBlock _time = new() { HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _phase = new() { HorizontalAlignment = HorizontalAlignment.Center, Opacity = .7 };
    private readonly Func<DateTime> _now;
    private TimeSpan _left;
    private DateTime? _ends;
    private bool _break;
    private TimeSpan _whole;

    public bool IsRunning => _ends != null;
    public bool IsBreak => _break;
    public TimeSpan Left => _ends is { } ends ? TimeSpan.FromTicks(Math.Max(0, (ends - _now()).Ticks)) : _left;

    public TimerWidgetView(DesktopWidget widget, Func<DateTime>? now = null) : base(widget, TimeSpan.FromMilliseconds(500))
    {
        _now = now ?? (() => DateTime.UtcNow);
        _toggle = Glyph("", "Start", 11, Toggle); _reset = Glyph("", "Reset", 11, Reset);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(_toggle); buttons.Children.Add(_reset);
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(_phase); middle.Children.Add(_time); middle.Children.Add(buttons);
        var root = new Grid { Width = 180, Height = 180 };
        root.Children.Add(_ring = new Ring(this)); root.Children.Add(middle);
        Content = new Viewbox { Child = root, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(this, "Focus timer");
        Reset();
        UpdateAppearance(widget);
    }

    private TimeSpan Length => TimeSpan.FromMinutes(_break ? Widget.TimerBreakMinutes : Widget.TimerMinutes);

    public void Toggle()
    {
        if (_ends != null) { _left = Left; _ends = null; }
        else _ends = _now() + (_left > TimeSpan.Zero ? _left : Length);
        Refresh();
    }

    public void Reset() { _break = false; _ends = null; _left = _whole = Length; Refresh(); }

    protected override void AppearanceChanged()
    {
        if (_ends == null && _left == _whole && _whole != Length) _left = _whole = Length; // A new length before it has started.
        _time.Foreground = _phase.Foreground = _toggle.Foreground = _reset.Foreground = Ink;
        _time.FontSize = 36; _phase.FontSize = 13;
    }

    protected override void Refresh()
    {
        if (_ends is { } ends && _now() >= ends)
        {
            _break = !_break; _whole = Length; _ends = _now() + Length;
            if (!PreviewMode) System.Media.SystemSounds.Asterisk.Play();
        }
        var left = PreviewMode ? TimeSpan.FromMinutes(Widget.TimerMinutes * .62) : Left;
        _time.Text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
        _phase.Text = _break ? "Break" : "Focus";
        _toggle.Content = _ends == null ? "" : "";
        _toggle.ToolTip = _ends == null ? "Start" : "Pause"; AutomationProperties.SetName(_toggle, (string)_toggle.ToolTip);
        _ring.Amount = _whole <= TimeSpan.Zero ? 0 : left / _whole;
        _ring.InvalidateVisual();
    }

    private sealed class Ring(TimerWidgetView owner) : FrameworkElement
    {
        public double Amount;
        protected override void OnRender(DrawingContext drawing)
        {
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            var faded = owner.Ink.Clone(); faded.Opacity = .15;
            drawing.DrawEllipse(null, new Pen(faded, 8), center, 84, 84);
            DrawArc(drawing, new Pen(owner.Accent, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, center, 84, Amount);
        }
    }
}
