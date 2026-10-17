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
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace WallpaperProfiles.UI;

internal sealed class ClockWidgetView : System.Windows.Controls.UserControl
{
    internal static string ScriptFont { get; } = new[] { "Edwardian Script ITC", "French Script MT", "Segoe Script", "Gabriola" }
        .FirstOrDefault(name => Fonts.SystemFontFamilies.Any(font => font.Source.Equals(name, StringComparison.OrdinalIgnoreCase))) ?? "Segoe UI";
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _day = Detail("ClockDay", 76, 1);
    private readonly TextBlock _date = Detail("ClockDate", 11, .68);
    private readonly TextBlock _time = Detail("ClockTime", 14, 1);
    private readonly TextBlock _greeting = Detail("ClockGreetingText", 11, .75);
    private readonly AudioSpectrumView _wave = new() { Width = 440, Height = 64, Margin = new Thickness(0, 16, 0, 0) };
    private readonly AnalogFace _analog = new() { Width = 200, Height = 200, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
    private readonly StackPanel _panel = new() { Width = 460, Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)) };
    private DesktopWidget _widget;
    private ClockSkin? _skin;
    private string _minute = "";

    public ClockWidgetView(DesktopWidget widget)
    {
        _widget = widget;
        Content = new Viewbox { Child = _panel, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
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

    private void BuildSkin(ClockSkin skin)
    {
        _panel.Children.Clear();
        foreach (var text in new[] { _day, _date, _time, _greeting })
        {
            if (text.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(text);
            text.Height = double.NaN; text.Margin = new Thickness(0, 5, 0, 0); text.TextWrapping = TextWrapping.NoWrap;
        }
        if (skin == ClockSkin.Split)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(_time);
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
            details.Children.Add(_day); details.Children.Add(_date); Grid.SetColumn(details, 1); grid.Children.Add(details);
            _panel.Children.Add(grid);
        }
        else
        {
            if (skin == ClockSkin.Analog) _panel.Children.Add(_analog);
            if (skin is ClockSkin.Script or ClockSkin.Editorial or ClockSkin.DateCard or ClockSkin.Digital or ClockSkin.Terminal) _panel.Children.Add(_day);
            _panel.Children.Add(_time);
            if (skin is ClockSkin.Stacked or ClockSkin.Words) _panel.Children.Add(_day);
            _panel.Children.Add(_date);
        }
        _panel.Children.Add(_greeting); _panel.Children.Add(_wave);
        _skin = skin;
    }

    public void UpdateAppearance(DesktopWidget widget)
    {
        _widget = widget;
        if (_skin != widget.ClockSkin) BuildSkin(widget.ClockSkin);
        Foreground = SceneController.TryParseAccent(widget.TextColor, out var color) ? new SolidColorBrush(color) : Brushes.White;
        var script = widget.ClockSkin == ClockSkin.Script;
        foreach (var text in new[] { _day, _date, _time, _greeting })
        {
            text.FontFamily = script && text != _day ? new FontFamily("Segoe UI") : new FontFamily(widget.FontFamily);
            text.FontWeight = widget.Bold ? FontWeights.Bold : FontWeights.Normal;
            text.FontStyle = widget.Italic ? FontStyles.Italic : FontStyles.Normal;
            text.TextAlignment = widget.Alignment switch { WidgetTextAlignment.Center => TextAlignment.Center, WidgetTextAlignment.Right => TextAlignment.Right, _ => TextAlignment.Left };
        }
        _day.FontSize = script ? widget.FontSize : widget.ClockSkin == ClockSkin.DateCard ? widget.FontSize * .65 : 14;
        _day.Height = script ? 112 : double.NaN;
        _time.FontSize = script ? 14 : widget.ClockSkin == ClockSkin.Analog ? 22 : widget.FontSize;
        _time.LineHeight = widget.ClockSkin == ClockSkin.Stacked ? widget.FontSize * .95 : double.NaN;
        _time.TextWrapping = widget.ClockSkin == ClockSkin.Words ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _date.FontSize = widget.ClockSkin == ClockSkin.DateCard ? 24 : 11;
        _greeting.Margin = new Thickness(0, 20, 0, 0);
        _greeting.Visibility = widget.ClockShowGreeting ? Visibility.Visible : Visibility.Collapsed;
        _wave.Height = widget.SpectrumSkin is SpectrumSkin.Ring or SpectrumSkin.Radial or SpectrumSkin.Arc ? 150 : 64;
        _wave.UpdateAppearance(widget.ClockShowVisualizer, Foreground, widget);
        _analog.Ink = Foreground;
        _minute = "";
        RefreshTime(DateTime.Now);
    }

    internal static string Greeting(DateTime now) => now.Hour switch
    {
        < 12 => "GOOD MORNING!", < 18 => "GOOD AFTERNOON!", _ => "GOOD EVENING!"
    };

    internal void RefreshTime(DateTime now)
    {
        if (_widget.ClockSkin == ClockSkin.Analog) { _analog.Time = now; _analog.InvalidateVisual(); }
        var key = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        if (_minute == key) return;
        _minute = key;
        _day.Text = now.ToString("dddd", CultureInfo.CurrentCulture);
        _date.Text = now.ToString("dd MMMM, yyyy", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
        _time.Text = now.ToString(_widget.ClockUse24Hour ? "HH:mm" : "hh:mm tt", CultureInfo.CurrentCulture);
        switch (_widget.ClockSkin)
        {
            case ClockSkin.Script: _date.Text = Spaced(_date.Text); _time.Text = Spaced(_time.Text); break;
            case ClockSkin.Stacked: _time.Text = now.ToString(_widget.ClockUse24Hour ? "HH\nmm" : "hh\nmm", CultureInfo.CurrentCulture); break;
            case ClockSkin.Digital: _day.Text = "L O C A L   T I M E"; break;
            case ClockSkin.Terminal: _day.Text = "[ " + _day.Text.ToUpper(CultureInfo.CurrentCulture) + " ]"; _time.Text = "> " + _time.Text; break;
            case ClockSkin.Split: _time.Text = now.ToString(_widget.ClockUse24Hour ? "HH\nmm" : "hh\nmm", CultureInfo.CurrentCulture); break;
            case ClockSkin.Words: _time.Text = Number(now.Hour % 12 == 0 ? 12 : now.Hour % 12) + "\n" + (now.Minute == 0 ? "o’clock" : now.Minute < 10 ? "oh " + Number(now.Minute) : Number(now.Minute)); break;
            case ClockSkin.DateCard: _day.Text = _day.Text.ToUpper(CultureInfo.CurrentCulture); _date.Text = now.ToString("MMMM dd", CultureInfo.CurrentCulture); break;
        }
        _greeting.Text = Greeting(now);
    }

    private static string Number(int number)
    {
        string[] small = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        string[] tens = { "", "", "twenty", "thirty", "forty", "fifty" };
        return number < 20 ? small[number] : tens[number / 10] + (number % 10 == 0 ? "" : " " + small[number % 10]);
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

    private sealed class AnalogFace : FrameworkElement
    {
        public DateTime Time { get; set; }
        public System.Windows.Media.Brush Ink { get; set; } = Brushes.White;
        protected override void OnRender(DrawingContext dc)
        {
            var pen = new Pen(Ink, 1.5);
            dc.DrawEllipse(null, pen, new Point(100, 100), 94, 94);
            for (var i = 0; i < 12; i++)
            {
                var angle = i * Math.PI / 6;
                dc.DrawLine(pen, new Point(100 + Math.Sin(angle) * 84, 100 - Math.Cos(angle) * 84), new Point(100 + Math.Sin(angle) * 90, 100 - Math.Cos(angle) * 90));
            }
            void Hand(double angle, double length, double width)
            {
                var radians = angle * Math.PI / 180;
                dc.DrawLine(new Pen(Ink, width) { EndLineCap = PenLineCap.Round }, new Point(100, 100), new Point(100 + Math.Sin(radians) * length, 100 - Math.Cos(radians) * length));
            }
            Hand(Time.Hour * 30 + Time.Minute * .5, 46, 4); Hand(Time.Minute * 6, 70, 2); Hand(Time.Second * 6, 76, .75);
            dc.DrawEllipse(Ink, null, new Point(100, 100), 3, 3);
        }
    }
}
