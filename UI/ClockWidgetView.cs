using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _day = new() { Name = "ClockDay", TextAlignment = TextAlignment.Center };
    private readonly TextBlock _date = Detail("ClockDate", 11, .68);
    private readonly TextBlock _time = Detail("ClockTime", 14, 1);
    private readonly TextBlock _greeting = Detail("ClockGreetingText", 11, .75);
    private readonly AudioSpectrumView _wave = new() { Width = 440, Height = 64, Margin = new Thickness(0, 16, 0, 0) };
    private DesktopWidget _widget;
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
        panel.Children.Add(_wave);
        Content = new Viewbox { Child = panel, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        UpdateAppearance(widget);
        _timer.Tick += (_, _) => RefreshTime(DateTime.Now);
        Loaded += (_, _) => { RefreshTime(DateTime.Now); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
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
        _wave.UpdateAppearance(widget.ClockShowVisualizer, Foreground);
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

}
