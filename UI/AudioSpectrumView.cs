using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WallpaperProfiles.Engine;

namespace WallpaperProfiles.UI;

internal sealed class AudioSpectrumView : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Line[] _bars = new Line[SpectrumAnalyzer.BarCount];
    private readonly float[] _levels = new float[SpectrumAnalyzer.BarCount];
    private DesktopAudioSpectrum? _audio;

    public AudioSpectrumView()
    {
        var wave = new System.Windows.Controls.Canvas { Width = 440, Height = 64 };
        for (var i = 0; i < _bars.Length; i++)
        {
            var x = 4 + i * 432d / (_bars.Length - 1);
            _bars[i] = new Line { X1 = x, X2 = x, Y1 = 31, Y2 = 33, StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = .82 };
            wave.Children.Add(_bars[i]);
        }
        Content = new Viewbox { Child = wave, Stretch = Stretch.Fill };
        System.Windows.Automation.AutomationProperties.SetName(this, "Live desktop audio spectrum");
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) Start(); else Stop(); };
    }

    internal void UpdateAppearance(bool enabled, System.Windows.Media.Brush color)
    {
        foreach (var bar in _bars) bar.Stroke = color;
        Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) Stop();
    }

    private void Start()
    {
        // Previews render the same bars without opening extra capture sessions.
        if (!IsVisible || Window.GetWindow(this) is not DesktopWidgetWindow) return;
        Tick(); _timer.Start();
    }

    private void Tick()
    {
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

    private void Stop()
    {
        _timer.Stop(); _audio?.Dispose(); _audio = null;
        Array.Clear(_levels);
        foreach (var bar in _bars) { bar.Y1 = 31; bar.Y2 = 33; }
    }
}
