using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace WallpaperProfiles.UI;

internal sealed class AudioSpectrumView : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly float[] _levels = new float[SpectrumAnalyzer.BarCount];
    private readonly SpectrumSurface _surface;
    private DesktopAudioSpectrum? _audio;
    private DesktopWidget _widget = new();
    private System.Windows.Media.Brush _color = System.Windows.Media.Brushes.White;
    public bool PreviewMode { get; set; }

    public AudioSpectrumView()
    {
        _surface = new SpectrumSurface(this) { Width = 440, Height = 160 };
        Content = new Viewbox { Child = _surface, Stretch = Stretch.Fill };
        Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
        System.Windows.Automation.AutomationProperties.SetName(this, "Live desktop audio spectrum");
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) Start(); else Stop(); };
    }

    internal void UpdateAppearance(bool enabled, System.Windows.Media.Brush color, DesktopWidget? widget = null)
    {
        _color = color;
        if (widget != null) _widget = widget;
        var radial = _widget.SpectrumSkin is SpectrumSkin.Ring or SpectrumSkin.Arc or SpectrumSkin.Radial;
        _surface.Width = radial ? 160 : 440;
        ((Viewbox)Content).Stretch = radial ? Stretch.Uniform : Stretch.Fill;
        Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) Stop();
        else if (IsLoaded) Start();
        if (PreviewMode && _audio == null) FillPreview();
        _surface.InvalidateVisual();
    }

    private void Start()
    {
        // Studio and catalogue previews never open an audio capture session.
        if (!IsVisible || (!PreviewMode && Window.GetWindow(this) is not DesktopWidgetWindow)) return;
        Tick(); _timer.Start();
    }

    private void FillPreview()
    {
        var time = Environment.TickCount64 / 1000d;
        for (var i = 0; i < _levels.Length; i++)
            _levels[i] = (float)(.12 + .55 * Math.Pow((Math.Sin(i * .31 + time * 2) + 1) / 2, 2)
                * (.5 + .5 * Math.Sin(i * .13 + time) * Math.Sin(i * .13 + time)));
    }

    private void Tick()
    {
        if (PreviewMode) FillPreview();
        else
        {
            _audio ??= new DesktopAudioSpectrum();
            var bands = _audio.Read();
            ToolTip = _audio.Error ?? "Live desktop audio · drag to move";
            for (var i = 0; i < _levels.Length; i++)
            {
                var target = Math.Clamp(bands[i] * _widget.SpectrumSensitivity, 0, 1);
                var rate = target > _levels[i] ? .6 : 1 - _widget.SpectrumSmoothing;
                _levels[i] += (float)((target - _levels[i]) * rate);
            }
        }
        _surface.InvalidateVisual();
    }

    private void Stop()
    {
        _timer.Stop(); _audio?.Dispose(); _audio = null;
        Array.Clear(_levels); _surface.InvalidateVisual();
    }

    private sealed class SpectrumSurface(AudioSpectrumView owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var count = Math.Clamp(owner._widget.SpectrumDensity, 12, 96);
            var thickness = Math.Clamp(owner._widget.SpectrumThickness, 1, 10);
            var pen = new Pen(owner._color, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var skin = owner._widget.SpectrumSkin;
            double Level(int i) => owner._levels[(int)Math.Round(i * (owner._levels.Length - 1d) / (count - 1))];
            if (skin is SpectrumSkin.Wave or SpectrumSkin.Line)
            {
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new Point(6, 80), false, false);
                    for (var i = 0; i < count; i++)
                    {
                        var x = 6 + i * 428d / (count - 1);
                        var y = skin == SpectrumSkin.Wave ? 80 + Math.Sin(i * Math.PI / 3) * Level(i) * 70 : 145 - Level(i) * 132;
                        path.LineTo(new Point(x, y), true, false);
                    }
                }
                dc.DrawGeometry(null, pen, geometry);
                return;
            }
            var radial = skin is SpectrumSkin.Ring or SpectrumSkin.Arc or SpectrumSkin.Radial;
            for (var i = 0; i < count; i++)
            {
                var level = Level(i);
                if (radial)
                {
                    var arc = skin == SpectrumSkin.Arc;
                    var angle = arc ? Math.PI + i * Math.PI / (count - 1) : -Math.PI / 2 + i * Math.PI * 2 / count;
                    var radius = skin == SpectrumSkin.Radial ? 8 : 45;
                    var length = 2 + level * (skin == SpectrumSkin.Radial ? 65 : 28);
                    var cy = arc ? 116 : 80;
                    dc.DrawLine(pen, new Point(80 + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius),
                        new Point(80 + Math.Cos(angle) * (radius + length), cy + Math.Sin(angle) * (radius + length)));
                }
                else
                {
                    var x = 6 + i * 428d / (count - 1);
                    var height = 2 + level * 134;
                    if (skin == SpectrumSkin.Dots)
                    {
                        for (var y = 145d; y >= 145 - height; y -= 10)
                            dc.DrawEllipse(owner._color, null, new Point(x, y), thickness, thickness);
                    }
                    else if (skin == SpectrumSkin.Bars) dc.DrawLine(pen, new Point(x, 148), new Point(x, 148 - height));
                    else dc.DrawLine(pen, new Point(x, 80 - height / 2), new Point(x, 80 + height / 2));
                }
            }
        }
    }
}
