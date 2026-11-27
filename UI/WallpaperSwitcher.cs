using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;

namespace WallpaperProfiles.UI;

/// <summary>
/// Shows the wallpapers of one profile over the desktop, to choose from with keys, wheel or mouse. Nothing changes
/// until one is chosen.
/// </summary>
internal sealed class WallpaperSwitcher : Window
{
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<WallpaperProfile> _profiles;
    private readonly Action<Guid, string> _choose;
    private readonly Func<WallpaperProfile, IReadOnlyList<string>> _media;
    private readonly Grid _root = new();
    private readonly Border _shade = new();
    private readonly Image _behind = new() { Stretch = Stretch.UniformToFill }, _behindNext = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly Border _caption = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly System.Drawing.Rectangle _screen;
    private WallpaperProfile _profile;
    private IntPtr _before;
    private bool _closing, _showProfile;
    private int _request;

    internal SwitcherDeck Deck { get; } = new();
    internal WallpaperProfile Profile => _profile;
    private bool CoversScreen => _settings.SwitcherBackdrop != SwitcherBackdrop.None || _settings.SwitcherLayout == SwitcherLayout.Grid;

    public WallpaperSwitcher(AppSettings settings, IReadOnlyList<WallpaperProfile> profiles, WallpaperProfile profile,
        Action<Guid, string> choose, System.Drawing.Rectangle screen, Func<WallpaperProfile, IReadOnlyList<string>>? media = null)
    {
        _settings = settings; _profiles = profiles; _profile = profile; _choose = choose; _screen = screen;
        _media = media ?? WallpaperEngine.GetProfileMedia;
        Title = "Belie wallpaper switcher";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; UseLayoutRounding = true; Focusable = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_text); row.Children.Add(_swatches);
        _text.Foreground = Brushes.White; _text.FontSize = 14;
        _caption.Child = row; _caption.Background = Brush(Color.FromArgb(200, 12, 13, 16));
        _caption.CornerRadius = new CornerRadius(16); _caption.Padding = new Thickness(16, 7, 16, 8); _caption.Margin = new Thickness(0, 0, 0, 18);
        _root.Children.Add(_behind); _root.Children.Add(_behindNext); _root.Children.Add(_shade); _root.Children.Add(Deck); _root.Children.Add(_caption);
        _root.Opacity = 0;
        Content = _root;
        Deck.Aspect = screen.Width / (double)Math.Max(1, screen.Height);
        Deck.Apply(SwitcherLook.From(settings), Outline());
        Deck.Chosen += Pick;
        Deck.SelectionChanged += () => { ShowCaption(); _settle.Stop(); _settle.Start(); };
        Deck.PictureRead += path => { if (path == Deck.SelectedPath) { ShowCaption(); ShowBehind(); } };
        _settle.Tick += (_, _) => { _settle.Stop(); ShowBehind(); };
        _shade.Background = settings.SwitcherBackdrop == SwitcherBackdrop.None ? Brushes.Transparent
            : Brush(Color.FromArgb((byte)(settings.SwitcherBackdrop == SwitcherBackdrop.Dimmed ? 170 : 70), 6, 7, 9));
        _shade.MouseLeftButtonUp += (_, _) => CloseSmoothly();
        Deck.MouseLeftButtonUp += (_, _) => CloseSmoothly(); // Cards handle their own clicks.
        SourceInitialized += (_, _) => PlaceWindow();
        // Placing it on a monitor with another scale changes its size once more.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceWindow);
        Deactivated += (_, _) => CloseSmoothly();
        PreviewKeyDown += Pressed;
        Closed += (_, _) => { _settle.Stop(); Deck.Close(); _behind.Source = _behindNext.Source = null; };
    }

    private static SolidColorBrush Brush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    private System.Windows.Media.Brush Outline() => HexColor.TryParse(_settings.SwitcherOutlineColor, out var color) ? Brush(color) : Brush(Color.FromArgb(220, 12, 13, 16));
    private static bool Animates => SystemParameters.ClientAreaAnimation;

    /// <param name="current">The wallpaper to begin at.</param>
    public async Task OpenAsync(string current)
    {
        var items = await Read(_profile);
        if (_closing || items == null) return;
        _before = NativeMethods.GetForegroundWindow();
        Show();
        Deck.Show(items, current);
        Activate();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Keyboard.Focus(this);
        ShowBehind();
        _root.BeginAnimation(OpacityProperty, Animates ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) : null);
        _root.Opacity = 1;
    }

    private async Task<IReadOnlyList<string>?> Read(WallpaperProfile profile)
    {
        var request = ++_request;
        var items = await Task.Run(() => _media(profile));
        return request == _request ? items : null;
    }

    private void PlaceWindow()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, (style | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        Deck.ScreenHeight = _screen.Height / scale;
        var bounds = _screen;
        if (!CoversScreen)
        {
            // Over the bare desktop, only the strip the cards need is covered, which is quicker to draw.
            var height = Math.Min(_screen.Height, (int)(_screen.Height * _settings.SwitcherSize * 1.25) + (int)(130 * scale));
            var top = _screen.Top + (int)((_screen.Height - height) * _settings.SwitcherPosition);
            bounds = new System.Drawing.Rectangle(_screen.Left, top, _screen.Width, height);
        }
        else if (_settings.SwitcherLayout != SwitcherLayout.Grid)
        {
            var shift = (_settings.SwitcherPosition - .5) * (1 - _settings.SwitcherSize * 1.1) * _screen.Height / scale * 2;
            Deck.Margin = new Thickness(0, Math.Max(0, shift), 0, Math.Max(0, -shift));
        }
        else Deck.Margin = new Thickness(_screen.Width / scale * .06, 40, _screen.Width / scale * .06, 70);
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, NativeMethods.SWP_NOACTIVATE);
    }

    private void Pressed(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        switch (key)
        {
            case Key.Left or Key.H or Key.A: Deck.MoveBy(-1); break;
            case Key.Right or Key.L or Key.D: Deck.MoveBy(1); break;
            case Key.Up or Key.K: Deck.MoveRows(-1); break;
            case Key.Down or Key.J: Deck.MoveRows(1); break;
            case Key.PageUp: Deck.MoveBy(-5); break;
            case Key.PageDown: Deck.MoveBy(5); break;
            case Key.Home: Deck.Select(0); break;
            case Key.End: Deck.Select(int.MaxValue); break;
            case Key.R: if (Deck.Items.Count > 1) Deck.MoveBy(Random.Shared.Next(1, Math.Min(Deck.Items.Count, 12))); break;
            case Key.Enter or Key.Space: Deck.Choose(); break;
            case Key.Tab: ChangeProfile((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); break;
            case Key.Escape: CloseSmoothly(); break;
            default: e.Handled = false; break;
        }
    }

    /// <summary>Looks through another profile. It is only switched to when one of its wallpapers is chosen.</summary>
    internal async void ChangeProfile(int by)
    {
        if (_profiles.Count < 2) return;
        var index = Math.Max(0, _profiles.ToList().FindIndex(profile => profile.Id == _profile.Id));
        _profile = _profiles[((index + by) % _profiles.Count + _profiles.Count) % _profiles.Count];
        _showProfile = true;
        try
        {
            var profile = _profile;
            if (await Read(profile) is { } items && !_closing) Deck.Show(items, _settings.ChosenWallpapers.GetValueOrDefault(profile.Id) ?? "");
        }
        catch (Exception ex) { Logger.Error("Reading a profile's wallpapers failed.", ex); }
    }

    private void ShowCaption()
    {
        var empty = Deck.Items.Count == 0;
        var show = _settings.SwitcherNames || _settings.SwitcherColors || _showProfile || empty;
        _caption.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        _text.Text = empty ? $"{_profile.Name} has no wallpapers yet" : string.Join("   ", new[]
        {
            _showProfile || _settings.SwitcherNames ? _profile.Name : "",
            _settings.SwitcherNames ? System.IO.Path.GetFileNameWithoutExtension(Deck.SelectedPath) : "",
            _settings.SwitcherNames || _showProfile ? $"{Deck.Selected + 1} of {Deck.Items.Count}" : ""
        }.Where(part => part.Length > 0));
        _swatches.Children.Clear();
        if (!_settings.SwitcherColors || Deck.Picture(Deck.SelectedPath) is not BitmapSource picture) return;
        var colors = WallpaperColors.From(WallpaperColors.Sample(picture, 48, 27));
        foreach (var color in new[] { colors.Background, colors.Text, colors.Accent })
            _swatches.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brush(color), Margin = new Thickness(_swatches.Children.Count == 0 && _text.Text.Length > 0 ? 14 : 5, 0, 0, 0) });
    }

    private void ShowBehind()
    {
        if (_settings.SwitcherBackdrop is SwitcherBackdrop.None or SwitcherBackdrop.Dimmed || Deck.Picture(Deck.SelectedPath) is not BitmapSource picture) return;
        ImageSource source = picture;
        if (_settings.SwitcherBackdrop == SwitcherBackdrop.Blurred)
        {
            // A tiny copy stretched over the screen is a blur that costs nothing.
            source = new TransformedBitmap(picture, new ScaleTransform(48d / picture.PixelWidth, 27d / picture.PixelHeight));
            source.Freeze();
        }
        RenderOptions.SetBitmapScalingMode(_behindNext, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(_behind, BitmapScalingMode.HighQuality);
        if (_behindNext.Opacity >= 1) _behind.Source = _behindNext.Source;
        _behindNext.Source = source;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Animates ? 180 : 0));
        _behindNext.BeginAnimation(OpacityProperty, fade);
    }

    private void Pick(string path)
    {
        if (_closing) return;
        var profile = _profile.Id;
        if (!_settings.SwitcherStaysOpen)
        {
            CloseSmoothly();
            // The transition is skipped while something covers the desktop, which this window must not count as.
            if (_before != IntPtr.Zero && NativeMethods.IsWindow(_before)) NativeMethods.SetForegroundWindow(_before);
        }
        _choose(profile, path);
    }

    internal void CloseSmoothly()
    {
        if (_closing) return;
        _closing = true;
        IsHitTestVisible = false;
        if (!Animates || !IsLoaded) { Close(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) => Close();
        _root.BeginAnimation(OpacityProperty, fade);
    }
}
