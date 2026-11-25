using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using WallpaperProfiles.Workspaces;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WallpaperProfiles.UI;

internal enum IslandFace { Idle, Track, Volume, Workspace }

/// <summary>
/// A pill with the clock and the music that grows past the edge of the bar for a moment, to show a new track, the
/// volume or the workspace.
/// </summary>
internal sealed class BarIsland : Canvas
{
    private readonly BarFrame _frame = new() { ClipToBounds = true };
    private readonly Grid _faces = new();
    private readonly StackPanel _idle = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly DockPanel _track = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly StackPanel _short = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
    private readonly TextBlock _clock = new() { Name = "TopBarIsland", VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _music = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Border _smallCover = new() { CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _largeCover = new() { CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _artist = new() { TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .65 };
    private readonly TextBlock _shortText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _meter = new() { Width = 120, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Border _meterFill = new() { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DispatcherTimer _back = new();
    private readonly Stopwatch _time = new();
    private TopBarSettings _settings = new();
    private BarLook _look = new();
    private double _idleHeight, _idleWidth, _width, _height, _fromWidth, _fromHeight, _toWidth, _toHeight;
    private bool _atBottom, _moving, _hovered;
    private NowPlayingState? _playing;
    private string _heard = "";
    private (int? Volume, bool Muted)? _volume;
    private int _workspace;

    public IslandFace Face { get; private set; }
    internal System.Windows.Size Goal => new(_toWidth, _toHeight);
    internal bool IsMoving => _moving;
    internal BarFrame Frame => _frame;

    public BarIsland()
    {
        VerticalAlignment = VerticalAlignment.Stretch;
        _smallCover.Margin = new Thickness(16, 0, 8, 0);
        _largeCover.Margin = new Thickness(0, 0, 12, 0);
        _idle.Children.Add(_clock); _idle.Children.Add(_music);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(_title); words.Children.Add(_artist);
        DockPanel.SetDock(_largeCover, Dock.Left);
        _track.Children.Add(_largeCover); _track.Children.Add(words);
        _meter.Child = _meterFill;
        _short.Children.Add(_shortText); _short.Children.Add(_meter);
        _faces.Children.Add(_idle); _faces.Children.Add(_track); _faces.Children.Add(_short);
        _frame.Child = _faces;
        Children.Add(_frame);
        _back.Tick += (_, _) => { _back.Stop(); Turn(_hovered && _settings.IslandOnHover && HasMusic ? IslandFace.Track : IslandFace.Idle); };
        _frame.MouseEnter += (_, _) => { _hovered = true; if (_settings.IslandOnHover && HasMusic && Face == IslandFace.Idle) Turn(IslandFace.Track); };
        _frame.MouseLeave += (_, _) => { _hovered = false; if (Face == IslandFace.Track && !_back.IsEnabled) { _back.Interval = TimeSpan.FromMilliseconds(250); _back.Start(); } };
        SizeChanged += (_, _) => Place();
        Unloaded += (_, _) => { _back.Stop(); StopMoving(); };
    }

    private bool HasMusic => _settings.IslandShowsMusic && _playing is { HasSession: true };

    /// <param name="height">The height of the bar.</param>
    public void Apply(TopBarSettings settings, BarLook look, Brush background, Brush text, Brush accent, double height, bool preview)
    {
        (_settings, _look, _atBottom) = (settings, look, settings.Edge == TopBarEdge.Bottom);
        _idleHeight = Math.Max(10, look.Height ?? height);
        Height = height;
        _frame.Background = look.Background ?? background;
        _frame.BorderBrush = look.BorderColor ?? accent;
        _frame.BorderThickness = look.Border ?? new Thickness(0);
        _frame.Opacity = look.Opacity ?? 1;
        _frame.Shape(look.Shape == BarShape.Notch, _atBottom);
        _frame.Cursor = System.Windows.Input.Cursors.Hand;
        // A clipped frame would lose the notch's shoulders.
        _frame.ClipToBounds = look.Shape != BarShape.Notch;
        var size = look.Size ?? settings.FontSize;
        _clock.FontSize = size * 1.1; _title.FontSize = size; _artist.FontSize = size * .85; _shortText.FontSize = size;
        _clock.Foreground = _title.Foreground = _artist.Foreground = _shortText.Foreground = look.Color ?? text;
        if (look.Font is { Length: > 0 } font) SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, BarFonts.Find(font)); else ClearValue(System.Windows.Documents.TextElement.FontFamilyProperty);
        _clock.FontWeight = look.Weight ?? FontWeights.Normal;
        _clock.Visibility = settings.IslandShowsClock ? Visibility.Visible : Visibility.Collapsed;
        _smallCover.Width = _smallCover.Height = Math.Max(10, _idleHeight * .62);
        var tall = Grown;
        _largeCover.Width = _largeCover.Height = Math.Max(12, tall * .68);
        _track.Margin = new Thickness(tall * .2, 0, tall * .32, 0);
        var color = look.IconColor ?? accent;
        foreach (var old in _music.Children.OfType<AudioSpectrumView>().ToArray()) _music.Children.Remove(old);
        foreach (var old in _track.Children.OfType<AudioSpectrumView>().ToArray()) _track.Children.Remove(old);
        if (_smallCover.Parent == null) _music.Children.Add(_smallCover);
        _music.Children.Add(Visualizer(color, settings.VisualizerWidth * .62, _idleHeight * .56, preview));
        var large = Visualizer(color, settings.VisualizerWidth * .62, tall * .5, preview);
        DockPanel.SetDock(large, Dock.Right);
        _track.Children.Insert(1, large);
        _meterFill.Background = color;
        _meter.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 255));
        _heard = Key(_playing);
        ShowMusic();
        Turn(Face, animate: false);
    }

    private AudioSpectrumView Visualizer(Brush color, double width, double height, bool preview)
    {
        var skin = _settings.VisualizerSkin is SpectrumSkin.Ring or SpectrumSkin.Arc or SpectrumSkin.Radial ? SpectrumSkin.Mirrored : _settings.VisualizerSkin;
        var view = new AudioSpectrumView { Bars = Math.Max(3, (int)Math.Round(_settings.VisualizerBars * .62)), PreviewMode = preview, Width = Math.Max(12, width), Height = Math.Max(6, height), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        view.UpdateAppearance(true, color, new DesktopWidget { SpectrumSkin = skin });
        view.ToolTip = null;
        return view;
    }

    private double Grown => Math.Max(_idleHeight, _idleHeight * _settings.IslandGrowth);

    public void UpdateClock(string text) { if (_clock.Text == text) return; _clock.Text = text; if (Face == IslandFace.Idle) Turn(IslandFace.Idle, animate: false); }

    private static string Key(NowPlayingState? state) => state is { HasSession: true } ? state.Title + "\n" + state.Artist : "";

    public void UpdateMusic(NowPlayingState? state)
    {
        var first = _playing == null;
        _playing = state;
        var key = Key(state);
        var changed = key.Length > 0 && key != _heard;
        _heard = key;
        ShowMusic();
        // Music that was already on when the bar started is not news.
        if (changed && !first && state!.IsPlaying && _settings.IslandOnTrack && _settings.IslandShowsMusic) Announce(IslandFace.Track);
        else if (!HasMusic && Face == IslandFace.Track) Turn(IslandFace.Idle);
        else Turn(Face, animate: Face == IslandFace.Idle);
    }

    private void ShowMusic()
    {
        _music.Visibility = HasMusic ? Visibility.Visible : Visibility.Collapsed;
        if (!HasMusic) return;
        if (_playing!.Artwork == null) _smallCover.Background = _largeCover.Background = _meterFill.Background;
        else if ((_smallCover.Background as ImageBrush)?.ImageSource != _playing.Artwork)
            _smallCover.Background = _largeCover.Background = new ImageBrush(_playing.Artwork) { Stretch = Stretch.UniformToFill };
        _smallCover.Opacity = _playing.IsPlaying ? 1 : .55;
        _title.Text = _playing.Title; _artist.Text = _playing.Artist;
        AutomationProperties.SetName(this, $"{_clock.Text}, {_playing.Title}, {_playing.Artist}");
    }

    public void UpdateVolume(int? volume, bool muted)
    {
        var changed = _volume is { } before && before != (volume, muted);
        _volume = (volume, muted);
        if (!changed || volume == null || !_settings.IslandOnVolume) return;
        _shortText.Text = muted ? "Muted" : $"Volume {volume}%";
        _meter.Visibility = Visibility.Visible;
        _meterFill.Width = _meter.Width * (muted ? 0 : volume.Value) / 100;
        Announce(IslandFace.Volume);
    }

    public void UpdateWorkspace(int index, string name)
    {
        var changed = _workspace != 0 && index != _workspace;
        _workspace = index;
        if (!changed || index == 0 || !_settings.IslandOnWorkspace) return;
        _shortText.Text = name;
        _meter.Visibility = Visibility.Collapsed;
        Announce(IslandFace.Workspace);
    }

    internal void Announce(IslandFace face)
    {
        _back.Stop();
        _back.Interval = TimeSpan.FromSeconds(face == IslandFace.Track ? _settings.IslandSeconds : Math.Min(1.6, _settings.IslandSeconds));
        Turn(face);
        _back.Start();
    }

    private void Turn(IslandFace face, bool animate = true)
    {
        Face = face;
        var limit = new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity);
        var padding = _look.Padding ?? new Thickness(_idleHeight * .6, 0, _idleHeight * .6, 0);
        _idle.Margin = new Thickness(padding.Left, 0, padding.Right, 0);
        _idle.Measure(limit);
        _idleWidth = Math.Max(_look.MinWidth ?? 0, Math.Max(_idleHeight, _idle.DesiredSize.Width));
        switch (face)
        {
            case IslandFace.Track: (_toWidth, _toHeight) = (_settings.IslandWidth, Grown); break;
            case IslandFace.Volume or IslandFace.Workspace:
                _short.Measure(limit);
                (_toWidth, _toHeight) = (_short.DesiredSize.Width + _idleHeight * 1.4, Math.Max(_idleHeight, Grown * .8)); break;
            default:
                (_toWidth, _toHeight) = (_idleWidth, _idleHeight); break;
        }
        animate &= _settings.AnimationsEnabled && SystemParameters.ClientAreaAnimation && _width > 0 && IsLoaded;
        if (!animate) { StopMoving(); (_width, _height) = (_toWidth, _toHeight); Fade(1); Place(); return; }
        if (Math.Abs(_width - _toWidth) < .5 && Math.Abs(_height - _toHeight) < .5 && !_moving) { Fade(1); return; }
        (_fromWidth, _fromHeight) = (_width, _height);
        _time.Restart();
        if (_moving) return;
        _moving = true;
        CompositionTarget.Rendering += Move;
    }

    private void StopMoving() { if (!_moving) return; _moving = false; CompositionTarget.Rendering -= Move; }

    private void Move(object? sender, EventArgs e)
    {
        var time = _time.Elapsed.TotalMilliseconds * _settings.AnimationSpeed;
        var progress = Math.Min(1, time / Motion.OpenTime);
        var eased = Motion.Open(progress);
        (_width, _height) = (Math.Max(1, _fromWidth + (_toWidth - _fromWidth) * eased), Math.Max(1, _fromHeight + (_toHeight - _fromHeight) * eased));
        Fade(Motion.Fade(Math.Min(1, time / Motion.FadeTime)));
        if (progress >= 1) { StopMoving(); (_width, _height) = (_toWidth, _toHeight); Fade(1); }
        Place();
    }

    private void Fade(double progress)
    {
        void Toward(UIElement face, bool shown)
        {
            face.Opacity = progress >= 1 ? shown ? 1 : 0 : face.Opacity + ((shown ? 1 : 0) - face.Opacity) * progress;
            // A hidden visualizer does not listen or draw.
            face.Visibility = shown || face.Opacity > 0 ? Visibility.Visible : Visibility.Hidden;
        }
        Toward(_idle, Face == IslandFace.Idle); Toward(_track, Face == IslandFace.Track); Toward(_short, Face is IslandFace.Volume or IslandFace.Workspace);
    }

    private void Place()
    {
        Width = Math.Max(1, Face == IslandFace.Idle ? _width : _idleWidth);
        _frame.Width = _width; _frame.Height = _height;
        var radius = _look.Radius ?? new CornerRadius(_settings.CornerRadius > 0 ? Math.Min(_height / 2, Math.Max(_settings.CornerRadius, _idleHeight / 2)) : 0);
        _frame.CornerRadius = radius;
        var slot = ActualHeight > 0 ? ActualHeight : Height;
        // The grown island keeps its near edge, unless it is a notch, which keeps to the screen's.
        var inset = _look.Shape == BarShape.Notch ? 0 : Math.Max(0, (slot - _idleHeight) / 2);
        SetLeft(_frame, (Width - _width) / 2);
        SetTop(_frame, _atBottom ? slot - inset - _height : inset);
    }
}
