using WallpaperProfiles.Models;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace WallpaperProfiles.UI;

internal sealed class NowPlayingView : System.Windows.Controls.UserControl
{
    private readonly Func<INowPlayingSource> _sourceFactory;
    private INowPlayingSource? _source;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _lifetime;
    private bool _reading, _commandPending, _updating, _seeking;
    private string? _commandError;
    private string _arrangement = "";
    private readonly ControlTemplate _minimalProgress = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
            <Grid>
            <Border Background="{DynamicResource BorderBrush}" Height="2" CornerRadius="1" VerticalAlignment="Center"/>
            <Track x:Name="PART_Track" VerticalAlignment="Center" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="{DynamicResource AccentBrush}" Height="2" CornerRadius="1"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb x:Name="SeekThumb" Width="10" Height="10" Opacity="0"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{DynamicResource AccentBrush}" CornerRadius="5"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="{DynamicResource BorderBrush}" Height="2" CornerRadius="1"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track>
            </Grid>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="SeekThumb" Property="Opacity" Value="1"/></Trigger>
                <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="SeekThumb" Property="Opacity" Value="1"/></Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    private DesktopWidget _widget;
    private NowPlayingState _state = new();
    private readonly Grid _layout = new();
    private readonly Border _art = new() { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 16, 0) };
    private readonly System.Windows.Controls.Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _placeholder = new() { Text = "♪", FontSize = 34, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = .6 };
    private readonly Grid _artFrame = new();
    private readonly System.Windows.Shapes.Ellipse _disc = new() { Fill = VinylFill(), Visibility = Visibility.Collapsed };
    private readonly RotateTransform _spin = new();
    private bool _spinning;
    private readonly ProgressRing _ring = new() { Visibility = Visibility.Collapsed };
    private readonly Border _backdrop = new() { IsHitTestVisible = false }, _shade = new() { IsHitTestVisible = false };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _content;
    private ImageSource? _artwork;
    private System.Windows.Media.Color? _artAccent;
    private readonly StackPanel _details = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _player = new() { FontSize = 11, Opacity = .75, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _track = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _artist = new() { FontSize = 12, Opacity = .85, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _previous, _toggle, _next;
    private readonly StackPanel _progress = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly AudioSpectrumView _spectrum = new() { Name = "NowPlayingAudioSpectrum", Height = 48, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Slider _position = new() { Minimum = 0, Maximum = 1, Height = 22, Margin = new Thickness(0) };
    private readonly TextBlock _elapsed = new() { FontSize = 11, Opacity = .75 };
    private readonly TextBlock _duration = new() { FontSize = 11, Opacity = .75, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
    private readonly TextBlock _message = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

    public NowPlayingView(DesktopWidget widget, Func<INowPlayingSource>? sourceFactory = null)
    {
        _widget = widget;
        _layout.Cursor = widget.Locked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll;
        _layout.ToolTip = widget.Locked ? "Unlock position in Widgets to move this widget." : "Drag the song text or album art to move this widget.";
        _controls.Cursor = System.Windows.Input.Cursors.Arrow;
        _sourceFactory = sourceFactory ?? (() => new WindowsNowPlayingSource());
        _previous = Transport("Previous track", "\uE892", PlaybackCommand.Previous);
        _toggle = Transport("Play", "\uE768", PlaybackCommand.Toggle);
        _next = Transport("Next track", "\uE893", PlaybackCommand.Next);
        _controls.Children.Add(_previous); _controls.Children.Add(_toggle); _controls.Children.Add(_next);
        var artwork = new Grid(); artwork.Children.Add(_image); artwork.Children.Add(_placeholder); _art.Child = artwork;
        _art.SizeChanged += (_, _) => { var radius = Round ? _art.ActualWidth / 2 : 10; artwork.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, _art.ActualWidth), Math.Max(0, _art.ActualHeight)), radius, radius); };
        _details.Children.Add(_player); _details.Children.Add(_track); _details.Children.Add(_artist); _details.Children.Add(_controls);
        AutomationProperties.SetName(_position, "Playback position");
        _position.ValueChanged += (_, _) => { if (!_updating && _seeking) _elapsed.Text = NowPlayingState.TimeLabel(TimeSpan.FromSeconds(_position.Value)); };
        _position.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _seeking = true));
        _position.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(async (_, _) =>
        {
            if (!_seeking) return;
            _seeking = false;
            await SendAsync(PlaybackCommand.Seek, _state.Start + TimeSpan.FromSeconds(_position.Value));
        }));
        _position.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
        _position.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            if (!_seeking) return;
            _seeking = false;
            await SendAsync(PlaybackCommand.Seek, _state.Start + TimeSpan.FromSeconds(_position.Value));
        };
        _position.LostMouseCapture += (_, _) => _seeking = false;
        _position.PreviewKeyDown += async (_, e) =>
        {
            var step = e.Key switch { Key.Left or Key.Down => -5, Key.Right or Key.Up => 5, _ => 0 };
            if (step == 0 || !_state.CanSeek) return;
            e.Handled = true;
            await SendAsync(PlaybackCommand.Seek, _state.Start + TimeSpan.FromSeconds(Math.Clamp(_position.Value + step, 0, _state.Duration.TotalSeconds)));
        };
        var times = new Grid(); times.Children.Add(_elapsed); times.Children.Add(_duration);
        _progress.Children.Add(_position); _progress.Children.Add(times);
        var content = _content = new StackPanel(); content.Children.Add(_layout); content.Children.Add(_progress); content.Children.Add(_spectrum); content.Children.Add(_message);
        _scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0) };
        _artFrame.RenderTransformOrigin = _disc.RenderTransformOrigin = _art.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        _artFrame.Children.Add(_disc); _artFrame.Children.Add(_art); _artFrame.Children.Add(_ring);
        _disc.RenderTransform = _spin; _art.RenderTransform = _spin;
        var root = new Grid(); root.Children.Add(_backdrop); root.Children.Add(_shade); root.Children.Add(_scroll);
        Content = root;
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { Start(); await RefreshAsync(); };
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); else if (IsLoaded) { Start(); _ = RefreshAsync(); } };
        SizeChanged += (_, _) => ArrangePlayer();
        UpdateAppearance(widget);
        ApplyState(new());
    }

    private Button Transport(string label, string glyph, PlaybackCommand command)
    {
        var button = new Button { Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
            Width = 36, Height = 36, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), ToolTip = label, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        AutomationProperties.SetName(button, label);
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Chrome";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center); presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(OpacityProperty, .7, "Chrome")); template.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.White, "Chrome")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, .35, "Chrome")); template.Triggers.Add(disabled);
        button.Template = template;
        button.Click += async (_, _) => await SendAsync(command, TimeSpan.Zero);
        return button;
    }

    internal void UpdateAppearance(DesktopWidget widget)
    {
        _widget = widget;
        _layout.Cursor = widget.Locked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll;
        _layout.ToolTip = widget.Locked ? "Unlock position in Widgets to move this widget." : "Drag the song text or album art to move this widget.";
        Foreground = ColorBrush(widget.TextColor, Colors.White);
        FontFamily = new FontFamily(widget.FontFamily);
        FontWeight = widget.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle = widget.Italic ? FontStyles.Italic : FontStyles.Normal;
        _track.FontSize = widget.FontSize;
        // A slanted last letter leans past where its line ends, and the edge of the card would cut it off.
        foreach (var line in new[] { _player, _track, _artist }) line.Padding = ItalicRoom(widget.Italic, line.FontSize);
        var accent = widget.MusicColorsFromArt && _artAccent is { } found ? new SolidColorBrush(found) : ColorBrush(widget.MusicAccentColor, Color.FromRgb(169, 201, 180));
        _ring.Color = accent; _ring.Track = new SolidColorBrush(Color.FromArgb(60, accent.Color.R, accent.Color.G, accent.Color.B));
        _spectrum.UpdateAppearance(widget.ShowAudioVisualizer, accent, widget);
        _toggle.Background = accent;
        _toggle.Foreground = AccentInkConverter.Luminance(accent.Color) < .179 ? Brushes.White : Brushes.Black;
        _previous.Foreground = _next.Foreground = Foreground;
        _position.Foreground = accent;
        _position.Resources["AccentBrush"] = accent;
        _position.Resources["AccentHoverBrush"] = accent;
        _position.Resources["BorderBrush"] = new SolidColorBrush(Color.FromArgb(70, accent.Color.R, accent.Color.G, accent.Color.B));
        if (widget.MusicLayout is NowPlayingLayout.Minimal or NowPlayingLayout.Cover or NowPlayingLayout.Vinyl) _position.Template = _minimalProgress;
        else _position.ClearValue(TemplateProperty);
        _player.Visibility = widget.MusicLayout is NowPlayingLayout.Minimal or NowPlayingLayout.Cover or NowPlayingLayout.Ring or NowPlayingLayout.Pill ? Visibility.Collapsed : Visibility.Visible;
        _position.IsMoveToPointEnabled = true;
        _art.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
        ArrangePlayer(); ApplyState(_state);
    }

    private void ArrangePlayer()
    {
        var arrangement = $"{_widget.MusicLayout}|{_widget.ShowAlbumArt}|{_widget.Alignment}|{_widget.Width}|{_widget.Height}|{ActualWidth}|{ActualHeight}|{_widget.MusicArtBackdrop}|{_widget.CornerRadius}|{_widget.ShowHeader}|{_widget.Title}";
        if (_arrangement == arrangement) return;
        _arrangement = arrangement;
        _layout.Children.Clear(); _layout.ColumnDefinitions.Clear(); _layout.RowDefinitions.Clear();
        var layout = _widget.MusicLayout;
        var stacked = layout is NowPlayingLayout.Stacked or NowPlayingLayout.Ring;
        // Cover's art is the whole card, so there is no separate picture.
        var art = layout is NowPlayingLayout.Vinyl or NowPlayingLayout.Ring || _widget.ShowAlbumArt && layout is not (NowPlayingLayout.Minimal or NowPlayingLayout.Cover);
        ArrangeBackdrop();
        _art.CornerRadius = new CornerRadius(Round ? 1000 : 10);
        _disc.Visibility = layout == NowPlayingLayout.Vinyl ? Visibility.Visible : Visibility.Collapsed;
        _ring.Visibility = layout == NowPlayingLayout.Ring ? Visibility.Visible : Visibility.Collapsed;
        _artFrame.Cursor = _layout.Cursor;
        if (_controls.Parent is System.Windows.Controls.Panel holder) holder.Children.Remove(_controls);
        _controls.Margin = layout == NowPlayingLayout.Pill ? new Thickness(8, 0, 0, 0) : new Thickness(0, 10, 0, 0);
        _controls.VerticalAlignment = VerticalAlignment.Center;
        var padded = FillsCard(_widget);
        _content.Margin = layout == NowPlayingLayout.Pill ? new Thickness(padded ? 12 : 0, padded ? 10 : 0, padded ? 12 : 0, padded ? 10 : 0) : new Thickness(padded ? 18 : 0);
        _content.VerticalAlignment = layout == NowPlayingLayout.Cover ? VerticalAlignment.Bottom : layout is NowPlayingLayout.Pill or NowPlayingLayout.Vinyl ? VerticalAlignment.Center : VerticalAlignment.Top;
        _scroll.VerticalScrollBarVisibility = layout is NowPlayingLayout.Pill or NowPlayingLayout.Cover or NowPlayingLayout.Vinyl ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        var center = stacked || _widget.Alignment == WidgetTextAlignment.Center;
        _track.TextAlignment = _artist.TextAlignment = _player.TextAlignment = _widget.Alignment.ToText();
        _controls.HorizontalAlignment = center ? System.Windows.HorizontalAlignment.Center : _widget.Alignment == WidgetTextAlignment.Right ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layout.ColumnDefinitions.Add(new ColumnDefinition());
        if (stacked)
        {
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_artFrame, 0); Grid.SetColumn(_artFrame, 0); Grid.SetColumnSpan(_artFrame, 2);
            Grid.SetRow(_details, 1); Grid.SetColumn(_details, 0); Grid.SetColumnSpan(_details, 2);
            SizeArt(layout == NowPlayingLayout.Ring ? Math.Clamp(Math.Min(_widget.Height * .5, _widget.Width - 48), 60, 260) : Math.Clamp(_widget.Height * .36, 60, 180));
            _artFrame.Margin = new Thickness(0, 0, 0, 12); _artFrame.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        }
        else
        {
            Grid.SetRow(_artFrame, 0); Grid.SetColumn(_artFrame, 0); Grid.SetColumnSpan(_artFrame, 1);
            Grid.SetRow(_details, 0); Grid.SetColumn(_details, 1); Grid.SetColumnSpan(_details, 1);
            var space = ActualWidth > 0 ? ActualWidth : _widget.Width - 32;
            SizeArt(layout == NowPlayingLayout.Vinyl ? Math.Clamp(Math.Min(space * .42, _widget.Height - 92), 64, 240)
                : layout == NowPlayingLayout.Pill ? Math.Clamp(_widget.Height - 28, 28, 64) : Math.Clamp(space * .26, 44, 100));
            _artFrame.Margin = new Thickness(0, 0, layout == NowPlayingLayout.Pill ? 12 : 16, 0); _artFrame.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        }
        if (layout == NowPlayingLayout.Pill)
        {
            _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_controls, 2); _layout.Children.Add(_controls);
        }
        else _details.Children.Add(_controls);
        _artFrame.Visibility = art ? Visibility.Visible : Visibility.Collapsed;
        _layout.Children.Add(_artFrame); _layout.Children.Add(_details);
        Spin();
    }

    private bool Round => _widget.MusicLayout is NowPlayingLayout.Vinyl or NowPlayingLayout.Ring or NowPlayingLayout.Pill;

    /// <summary>These reach the edges of the widget, so it adds no margin of its own.</summary>
    internal static bool FillsCard(NowPlayingLayout layout) => layout is NowPlayingLayout.Cover or NowPlayingLayout.Pill;
    internal static bool FillsCard(DesktopWidget widget) => FillsCard(widget.MusicLayout) || widget.MusicArtBackdrop;

    private void SizeArt(double size)
    {
        _artFrame.Width = _artFrame.Height = size;
        var inner = _widget.MusicLayout switch { NowPlayingLayout.Vinyl => size * .46, NowPlayingLayout.Ring => size - 24, _ => size };
        _art.Width = _art.Height = Math.Max(10, inner);
        _art.Margin = new Thickness(0);
        _art.HorizontalAlignment = System.Windows.HorizontalAlignment.Center; _art.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.FontSize = Math.Min(34, inner * .5);
    }

    private void ArrangeBackdrop()
    {
        var cover = _widget.MusicLayout == NowPlayingLayout.Cover;
        var radius = new CornerRadius(_widget.MusicLayout == NowPlayingLayout.Pill ? Math.Max(_widget.CornerRadius, _widget.Height / 2) : _widget.CornerRadius);
        _backdrop.CornerRadius = _shade.CornerRadius = radius;
        _backdrop.Background = cover && _artwork != null ? new ImageBrush(_artwork) { Stretch = Stretch.UniformToFill }
            : _widget.MusicArtBackdrop && _artwork is System.Windows.Media.Imaging.BitmapSource image ? Blurred(image)
            : cover || _widget.MusicLayout == NowPlayingLayout.Pill ? ColorBrush(_widget.BackgroundColor, Color.FromRgb(29, 32, 30)) : null;
        _shade.Background = cover ? new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(20, 0, 0, 0), 0), new(Color.FromArgb(90, 0, 0, 0), .45), new(Color.FromArgb(225, 0, 0, 0), 1) }, 90)
            : _widget.MusicArtBackdrop && _artwork != null ? new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) : null;
    }

    /// <summary>A tiny copy that WPF smooths while enlarging; a blur effect would cost far more to draw.</summary>
    private static ImageBrush? Blurred(System.Windows.Media.Imaging.BitmapSource image)
    {
        const int size = 12;
        var pixels = WallpaperColors.Sample(image, size, size);
        if (pixels.Length != size * size) return null;
        var smooth = new int[pixels.Length];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                int red = 0, green = 0, blue = 0, count = 0;
                for (var v = Math.Max(0, y - 1); v <= Math.Min(size - 1, y + 1); v++)
                    for (var h = Math.Max(0, x - 1); h <= Math.Min(size - 1, x + 1); h++)
                    { var pixel = pixels[v * size + h]; red += pixel >> 16 & 255; green += pixel >> 8 & 255; blue += pixel & 255; count++; }
                smooth[y * size + x] = red / count << 16 | green / count << 8 | blue / count;
            }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgr32, null, smooth, size * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.Linear);
        return brush;
    }

    private static System.Windows.Media.Brush VinylFill()
    {
        var stops = new GradientStopCollection();
        for (var i = 0; i <= 20; i++) stops.Add(new GradientStop(i % 2 == 0 ? Color.FromRgb(12, 12, 14) : Color.FromRgb(52, 52, 58), i / 20d));
        var brush = new RadialGradientBrush(stops);
        brush.Freeze();
        return brush;
    }

    /// <summary>The record turns while music plays and stops where it is.</summary>
    private void Spin()
    {
        var spin = _widget.MusicLayout == NowPlayingLayout.Vinyl && _state.IsPlaying && IsVisible && IsLoaded && SystemParameters.ClientAreaAnimation;
        if (spin == _spinning) return;
        _spinning = spin;
        if (!spin)
        {
            var angle = _spin.Angle;
            _spin.BeginAnimation(RotateTransform.AngleProperty, null);
            _spin.Angle = _widget.MusicLayout == NowPlayingLayout.Vinyl ? angle : 0;
            return;
        }
        var turn = new System.Windows.Media.Animation.DoubleAnimation(_spin.Angle % 360, _spin.Angle % 360 + 360, TimeSpan.FromSeconds(6))
        { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(turn, 30);
        _spin.BeginAnimation(RotateTransform.AngleProperty, turn);
    }

    private sealed class ProgressRing : FrameworkElement
    {
        public System.Windows.Media.Brush Color = Brushes.White, Track = Brushes.Gray;
        public double Amount;
        protected override void OnRender(DrawingContext drawing)
        {
            var center = new System.Windows.Point(ActualWidth / 2, ActualHeight / 2);
            var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 4);
            drawing.DrawEllipse(null, new System.Windows.Media.Pen(Track, 5), center, radius, radius);
            InfoWidgetView.DrawArc(drawing, new System.Windows.Media.Pen(Color, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, center, radius, Amount);
        }
    }

    private void Start()
    {
        if (_source != null) return;
        _lifetime = new CancellationTokenSource(); _source = _sourceFactory(); _timer.Start();
    }
    private void Stop()
    {
        _timer.Stop(); _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        _source?.Dispose(); _source = null;
        Spin();
    }

    internal async Task RefreshAsync()
    {
        if (_reading || _commandPending || _source == null || _lifetime == null) return;
        _reading = true;
        var source = _source; var token = _lifetime.Token;
        try
        {
            var state = await source.ReadAsync(token);
            if (!token.IsCancellationRequested && ReferenceEquals(source, _source)) ApplyState(state);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch { if (!token.IsCancellationRequested) ApplyState(new NowPlayingState { Title = "Playback unavailable", Artist = "Try reopening your music player." }); }
        finally { _reading = false; }
    }

    internal void ApplyState(NowPlayingState state)
    {
        _state = state;
        _track.Text = state.Title; _track.ToolTip = state.Title;
        _artist.Text = state.Artist; _artist.ToolTip = string.Join(" · ", new[] { state.Artist, state.Album }.Where(s => !string.IsNullOrWhiteSpace(s)));
        _player.Text = state.HasSession ? state.Player + (state.IsPlaying ? " · Playing" : " · Paused") : "NOW PLAYING";
        if (!ReferenceEquals(_artwork, state.Artwork))
        {
            _artwork = state.Artwork;
            _artAccent = null;
            if (_artwork is System.Windows.Media.Imaging.BitmapSource cover && WallpaperColors.Sample(cover, 32, 32) is { Length: > 0 } pixels) _artAccent = WallpaperColors.From(pixels).Accent;
            ArrangeBackdrop();
            if (_widget.MusicColorsFromArt) { UpdateAppearance(_widget); return; } // Comes back here with the new color.
        }
        _image.Source = state.Artwork; _placeholder.Visibility = state.Artwork == null ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Content = state.IsPlaying ? "\uE769" : "\uE768";
        var label = state.IsPlaying ? "Pause" : "Play"; _toggle.ToolTip = label; AutomationProperties.SetName(_toggle, label);
        _toggle.IsEnabled = state.CanToggle && !_commandPending;
        _previous.IsEnabled = state.CanPrevious && !_commandPending; _next.IsEnabled = state.CanNext && !_commandPending;
        _controls.Visibility = _widget.ShowPlaybackControls ? Visibility.Visible : Visibility.Collapsed;
        _ring.Amount = state.Duration > TimeSpan.Zero ? state.Elapsed(DateTimeOffset.UtcNow) / state.Duration : 0; _ring.InvalidateVisual();
        Spin();
        _progress.Visibility = _widget.ShowPlaybackProgress && state.Duration > TimeSpan.Zero && _widget.MusicLayout is not (NowPlayingLayout.Ring or NowPlayingLayout.Pill) ? Visibility.Visible : Visibility.Collapsed;
        _position.IsEnabled = state.CanSeek && !_commandPending;
        _updating = true;
        if (!_seeking) { _position.Maximum = Math.Max(1, state.Duration.TotalSeconds); _position.Value = state.Elapsed(DateTimeOffset.UtcNow).TotalSeconds; }
        _updating = false;
        _elapsed.Text = NowPlayingState.TimeLabel(TimeSpan.FromSeconds(_position.Value));
        _duration.Text = NowPlayingState.TimeLabel(state.Duration);
        AutomationProperties.SetName(_track, "Current song: " + state.Title);
        _message.Text = state.Error ?? _commandError ?? ""; _message.Visibility = _message.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    internal async Task SendAsync(PlaybackCommand command, TimeSpan position)
    {
        if (_commandPending || _source == null || _lifetime == null) return;
        var allowed = command switch { PlaybackCommand.Toggle => _state.CanToggle, PlaybackCommand.Previous => _state.CanPrevious, PlaybackCommand.Next => _state.CanNext, _ => _state.CanSeek };
        if (!allowed) return;
        var source = _source; var token = _lifetime.Token;
        _commandPending = true; ApplyState(_state);
        try
        {
            if (command == PlaybackCommand.Seek) position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, _state.Start.Ticks, Math.Max(_state.Start.Ticks, _state.End.Ticks)));
            var success = await source.SendAsync(command, position, token);
            if (!token.IsCancellationRequested) _commandError = success ? null : "The player could not perform that action.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { if (!token.IsCancellationRequested) _commandError = "Could not control playback. Try again."; }
        finally { _commandPending = false; _toggle.IsEnabled = _state.CanToggle; _previous.IsEnabled = _state.CanPrevious; _next.IsEnabled = _state.CanNext; _position.IsEnabled = _state.CanSeek; }
        ApplyState(_state);
        await RefreshAsync();
    }

    internal static Thickness ItalicRoom(bool italic, double fontSize) => new(0, 0, italic ? Math.Ceiling(fontSize * .25) : 0, 0);

    private static SolidColorBrush ColorBrush(string value, Color fallback) => new(SceneController.TryParseAccent(value, out var color) ? color : fallback);
}
