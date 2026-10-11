using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace WallpaperProfiles.UI;

internal sealed class ClockWidgetView : System.Windows.Controls.UserControl
{
    internal static string ScriptFont { get; } = new[] { "Edwardian Script ITC", "French Script MT", "Segoe Script", "Gabriola" }
        .FirstOrDefault(name => Fonts.SystemFontFamilies.Any(font => font.Source.Equals(name, StringComparison.OrdinalIgnoreCase))) ?? "Segoe UI";
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly TextBlock _day = new() { Name = "ClockDay", TextAlignment = TextAlignment.Center };
    private readonly TextBlock _date = Detail("ClockDate", 11, .68);
    private readonly TextBlock _time = Detail("ClockTime", 14, 1);
    private readonly TextBlock _greeting = Detail("ClockGreetingText", 11, .75);
    private readonly System.Windows.Controls.Canvas _wave = new() { Width = 440, Height = 64, Margin = new Thickness(0, 16, 0, 0) };
    private readonly Line[] _bars = new Line[SpectrumAnalyzer.BarCount];
    private readonly float[] _levels = new float[SpectrumAnalyzer.BarCount];
    private DesktopWidget _widget;
    private DesktopAudioSpectrum? _audio;
    private string _minute = "";

    public ClockWidgetView(DesktopWidget widget)
    {
        _widget = widget;
        // A tiny alpha makes the transparent spaces draggable without a visible card.
        var panel = new StackPanel { Width = 460, Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)) };
        _day.Height = 112;
        _date.Margin = new Thickness(0, 9, 0, 0);
        _time.Margin = new Thickness(0, 4, 0, 0);
        _greeting.Margin = new Thickness(0, 20, 0, 0);
        panel.Children.Add(_day); panel.Children.Add(_date); panel.Children.Add(_time); panel.Children.Add(_greeting);
        for (var i = 0; i < _bars.Length; i++)
        {
            var x = 4 + i * 432d / (_bars.Length - 1);
            _bars[i] = new Line { X1 = x, X2 = x, Y1 = 31, Y2 = 33, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = .82 };
            _wave.Children.Add(_bars[i]);
        }
        panel.Children.Add(_wave);
        Content = new Viewbox { Child = panel, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        UpdateAppearance(widget);
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { Tick(); _timer.Start(); };
        Unloaded += (_, _) => { _timer.Stop(); StopAudio(); };
    }

    private static TextBlock Detail(string name, double size, double opacity) => new()
    {
        Name = name, FontFamily = new FontFamily("Segoe UI"), FontSize = size, FontWeight = FontWeights.Normal,
        FontStyle = FontStyles.Normal, TextAlignment = TextAlignment.Center, Opacity = opacity
    };

    public void UpdateAppearance(DesktopWidget widget)
    {
        _widget = widget;
        Foreground = SceneController.TryParseAccent(widget.TextColor, out var color) ? new SolidColorBrush(color) : Brushes.White;
        _day.FontFamily = new FontFamily(widget.FontFamily); _day.FontSize = widget.FontSize;
        _day.FontWeight = widget.Bold ? FontWeights.Bold : FontWeights.Normal;
        _day.FontStyle = widget.Italic ? FontStyles.Italic : FontStyles.Normal;
        var alignment = widget.Alignment switch { WidgetTextAlignment.Center => TextAlignment.Center, WidgetTextAlignment.Right => TextAlignment.Right, _ => TextAlignment.Left };
        foreach (var text in new[] { _day, _date, _time, _greeting }) text.TextAlignment = alignment;
        _greeting.Visibility = widget.ClockShowGreeting ? Visibility.Visible : Visibility.Collapsed;
        _wave.Visibility = widget.ClockShowVisualizer ? Visibility.Visible : Visibility.Collapsed;
        foreach (var bar in _bars) bar.Stroke = Foreground;
        if (!widget.ClockShowVisualizer) StopAudio();
        _minute = "";
        RefreshTime(DateTime.Now);
    }

    internal static string Greeting(DateTime now) => now.Hour switch
    {
        < 12 => "GOOD MORNING!", < 18 => "GOOD AFTERNOON!", _ => "GOOD EVENING!"
    };

    internal void RefreshTime(DateTime now)
    {
        var key = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        if (_minute == key) return;
        _minute = key;
        _day.Text = now.ToString("dddd", CultureInfo.CurrentCulture);
        _date.Text = Spaced(now.ToString("dd MMMM, yyyy", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture));
        _time.Text = Spaced(now.ToString(_widget.ClockUse24Hour ? "HH:mm" : "hh:mm tt", CultureInfo.CurrentCulture));
        _greeting.Text = Greeting(now);
    }

    private static string Spaced(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        var spaced = new System.Text.StringBuilder();
        while (elements.MoveNext())
        {
            if (spaced.Length > 0) spaced.Append('\u2009');
            spaced.Append(elements.GetTextElement());
        }
        return spaced.ToString();
    }

    private void Tick()
    {
        RefreshTime(DateTime.Now);
        // Gallery and editor previews never open additional audio capture sessions.
        if (!_widget.ClockShowVisualizer || Window.GetWindow(this) is not DesktopWidgetWindow) return;
        _audio ??= new DesktopAudioSpectrum();
        var bands = _audio.Read();
        ToolTip = _audio.Error ?? "Live desktop audio · drag to move";
        for (var i = 0; i < _bars.Length; i++)
        {
            _levels[i] += (bands[i] - _levels[i]) * (bands[i] > _levels[i] ? .6f : .16f);
            var half = 1 + 29 * _levels[i];
            _bars[i].Y1 = 32 - half; _bars[i].Y2 = 32 + half;
        }
    }

    private void StopAudio()
    {
        _audio?.Dispose(); _audio = null;
        Array.Clear(_levels);
        foreach (var bar in _bars) { bar.Y1 = 31; bar.Y2 = 33; }
    }
}
