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
    private readonly StackPanel _details = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _player = new() { FontSize = 11, Opacity = .75, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _track = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _artist = new() { FontSize = 12, Opacity = .85, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _previous, _toggle, _next;
    private readonly StackPanel _progress = new() { Margin = new Thickness(0, 10, 0, 0) };
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
        _art.SizeChanged += (_, _) => artwork.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, _art.ActualWidth), Math.Max(0, _art.ActualHeight)), 10, 10);
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
        var content = new StackPanel(); content.Children.Add(_layout); content.Children.Add(_progress); content.Children.Add(_message);
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0) };
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
        var accent = ColorBrush(widget.MusicAccentColor, Color.FromRgb(169, 201, 180));
        _toggle.Background = accent;
        _toggle.Foreground = AccentInkConverter.Luminance(accent.Color) < .179 ? Brushes.White : Brushes.Black;
        _previous.Foreground = _next.Foreground = Foreground;
        _position.Foreground = accent;
        _position.Resources["AccentBrush"] = accent;
        _position.Resources["AccentHoverBrush"] = accent;
        _position.Resources["BorderBrush"] = new SolidColorBrush(Color.FromArgb(70, accent.Color.R, accent.Color.G, accent.Color.B));
        if (widget.MusicLayout == NowPlayingLayout.Minimal) _position.Template = _minimalProgress;
        else _position.ClearValue(TemplateProperty);
        _player.Visibility = widget.MusicLayout == NowPlayingLayout.Minimal ? Visibility.Collapsed : Visibility.Visible;
        _position.IsMoveToPointEnabled = true;
        _art.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
        ArrangePlayer(); ApplyState(_state);
    }

    private void ArrangePlayer()
    {
        var arrangement = $"{_widget.MusicLayout}|{_widget.ShowAlbumArt}|{_widget.Alignment}|{_widget.Width}|{_widget.Height}|{ActualWidth}";
        if (_arrangement == arrangement) return;
        _arrangement = arrangement;
        _layout.Children.Clear(); _layout.ColumnDefinitions.Clear(); _layout.RowDefinitions.Clear();
        var stacked = _widget.MusicLayout == NowPlayingLayout.Stacked;
        var art = _widget.ShowAlbumArt && _widget.MusicLayout != NowPlayingLayout.Minimal;
        var center = stacked || _widget.Alignment == WidgetTextAlignment.Center;
        _track.TextAlignment = _artist.TextAlignment = _player.TextAlignment = (System.Windows.TextAlignment)_widget.Alignment;
        _controls.HorizontalAlignment = center ? System.Windows.HorizontalAlignment.Center : _widget.Alignment == WidgetTextAlignment.Right ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layout.ColumnDefinitions.Add(new ColumnDefinition());
        if (stacked)
        {
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_art, 0); Grid.SetColumn(_art, 0); Grid.SetColumnSpan(_art, 2);
            Grid.SetRow(_details, 1); Grid.SetColumn(_details, 0); Grid.SetColumnSpan(_details, 2);
            _art.Width = _art.Height = Math.Clamp(_widget.Height * .36, 60, 180);
            _art.Margin = new Thickness(0, 0, 0, 12); _art.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        }
        else
        {
            Grid.SetRow(_art, 0); Grid.SetColumn(_art, 0); Grid.SetColumnSpan(_art, 1);
            Grid.SetRow(_details, 0); Grid.SetColumn(_details, 1); Grid.SetColumnSpan(_details, 1);
            _art.Width = _art.Height = Math.Clamp((ActualWidth > 0 ? ActualWidth : _widget.Width - 32) * .26, 44, 100);
            _art.Margin = new Thickness(0, 0, 16, 0); _art.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        }
        _art.Visibility = art ? Visibility.Visible : Visibility.Collapsed;
        _layout.Children.Add(_art); _layout.Children.Add(_details);
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
        _image.Source = state.Artwork; _placeholder.Visibility = state.Artwork == null ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Content = state.IsPlaying ? "\uE769" : "\uE768";
        var label = state.IsPlaying ? "Pause" : "Play"; _toggle.ToolTip = label; AutomationProperties.SetName(_toggle, label);
        _toggle.IsEnabled = state.CanToggle && !_commandPending;
        _previous.IsEnabled = state.CanPrevious && !_commandPending; _next.IsEnabled = state.CanNext && !_commandPending;
        _controls.Visibility = _widget.ShowPlaybackControls ? Visibility.Visible : Visibility.Collapsed;
        _progress.Visibility = _widget.ShowPlaybackProgress && state.Duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
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

    private static SolidColorBrush ColorBrush(string value, Color fallback) => new(SceneController.TryParseAccent(value, out var color) ? color : fallback);
}
