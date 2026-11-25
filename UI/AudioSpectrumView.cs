using WallpaperProfiles.Models;
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
    /// <summary>Draws this many whole bars in the space there is, rather than stretching a picture of many.</summary>
    public int Bars { get; init; }

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
        if (Bars > 0) { if (Content != _surface) { ((Viewbox)Content).Child = null; Content = _surface; } _surface.Width = _surface.Height = double.NaN; }
        else ((Viewbox)Content).Stretch = radial ? Stretch.Uniform : Stretch.Fill;
        Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) Stop();
        else if (IsLoaded) Start();
        if (PreviewMode && _audio == null) FillPreview();
        _surface.InvalidateVisual();
    }

    private void Start()
    {
        // Studio and catalogue previews never open an audio capture session.
        if (!IsVisible || (!PreviewMode && Window.GetWindow(this) is not (DesktopWidgetWindow or TopBarWindow))) return;
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
            _audio ??= DesktopAudioSpectrum.Rent();
            var bands = _audio.Read();
            var changed = false;
            ToolTip = _audio.Error ?? "Live desktop audio · drag to move";
            for (var i = 0; i < _levels.Length; i++)
            {
                var target = Math.Clamp(bands[i] * _widget.SpectrumSensitivity, 0, 1);
                var rate = target > _levels[i] ? .6 : 1 - _widget.SpectrumSmoothing;
                var step = (float)((target - _levels[i]) * rate);
                if (Math.Abs(step) < .001f) continue;
                _levels[i] += step; changed = true;
            }
            if (!changed) return; // Silence is not worth drawing the whole bar or widget again for.
        }
        _surface.InvalidateVisual();
    }

    private void Stop()
    {
        _timer.Stop();
        if (_audio != null) { _audio = null; DesktopAudioSpectrum.Return(); }
        Array.Clear(_levels); _surface.InvalidateVisual();
    }

    private sealed class SpectrumSurface(AudioSpectrumView owner) : FrameworkElement
    {
        private void DrawBars(DrawingContext dc)
        {
            double width = RenderSize.Width, height = RenderSize.Height;
            if (width < 4 || height < 4) return;
            var count = owner.Bars;
            var skin = owner._widget.SpectrumSkin;
            var step = width / count;
            var thick = Math.Max(1, Math.Min(step * .55, height));
            var points = new Point[count];
            for (var i = 0; i < count; i++)
            {
                // The loudest of the bands a bar stands for.
                int from = i * owner._levels.Length / count, to = Math.Max(from + 1, (i + 1) * owner._levels.Length / count);
                double level = 0;
                for (var band = from; band < to && band < owner._levels.Length; band++) level = Math.Max(level, owner._levels[band]);
                var tall = thick + level * (height - thick);
                var x = i * step + (step - thick) / 2;
                points[i] = new Point(i * step + step / 2, height - thick / 2 - level * (height - thick));
                if (skin is SpectrumSkin.Wave or SpectrumSkin.Line) continue;
                if (skin == SpectrumSkin.Dots)
                {
                    for (var y = height - thick; y >= height - tall - .5; y -= thick * 1.4) dc.DrawEllipse(owner._color, null, new Point(x + thick / 2, y + thick / 2), thick / 2, thick / 2);
                    continue;
                }
                dc.DrawRoundedRectangle(owner._color, null, new Rect(x, skin == SpectrumSkin.Bars ? height - tall : (height - tall) / 2, thick, tall), thick / 2, thick / 2);
            }
            if (skin is not (SpectrumSkin.Wave or SpectrumSkin.Line)) return;
            var line = new StreamGeometry();
            using (var path = line.Open())
            {
                path.BeginFigure(new Point(points[0].X, skin == SpectrumSkin.Wave ? height : points[0].Y), skin == SpectrumSkin.Wave, false);
                for (var i = skin == SpectrumSkin.Wave ? 0 : 1; i < count; i++)
                {
                    var before = i == 0 ? points[0] : points[i - 1];
                    var middle = (before.X + points[i].X) / 2;
                    path.BezierTo(new Point(middle, before.Y), new Point(middle, points[i].Y), points[i], true, true);
                }
                if (skin == SpectrumSkin.Wave) path.LineTo(new Point(points[^1].X, height), true, false);
            }
            dc.DrawGeometry(skin == SpectrumSkin.Wave ? owner._color : null, skin == SpectrumSkin.Wave ? null : new Pen(owner._color, Math.Max(1.5, thick * .3)), line);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (owner.Bars > 0) { DrawBars(dc); return; }
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
