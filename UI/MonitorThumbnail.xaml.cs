using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WallpaperProfiles.Models;
using System.Windows.Media.Imaging;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WallpaperProfiles.UI;

public partial class MonitorThumbnail : System.Windows.Controls.UserControl
{
    private const double ScreenOpacity = 1.0;

    private static readonly SolidColorBrush AccentBrush = new(System.Windows.Media.Color.FromRgb(0x83, 0xAD, 0xB7));
    private static readonly SolidColorBrush InactiveBrush = new(System.Windows.Media.Color.FromRgb(0x41, 0x4C, 0x5A));

    public static readonly DependencyProperty PreviewFitProperty = DependencyProperty.Register(nameof(PreviewFit),
        typeof(FitMode), typeof(MonitorThumbnail), new PropertyMetadata(FitMode.Fill, OnChanged));
    public FitMode PreviewFit { get => (FitMode)GetValue(PreviewFitProperty); set => SetValue(PreviewFitProperty, value); }
    public static readonly DependencyProperty PreviewFilePathProperty = DependencyProperty.Register(nameof(PreviewFilePath),
        typeof(string), typeof(MonitorThumbnail), new PropertyMetadata("", FileChanged));
    public string PreviewFilePath { get => (string)GetValue(PreviewFilePathProperty); set => SetValue(PreviewFilePathProperty, value); }
    private System.Windows.Size _pixelSize;
    private int _fileVersion;
    private static async void FileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var preview = (MonitorThumbnail)d;
        var version = ++preview._fileVersion;
        preview._pixelSize = default;
        var size = await Task.Run(() =>
        {
            try
            {
                using var stream = System.IO.File.OpenRead(e.NewValue as string ?? "");
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                return new System.Windows.Size(frame.PixelWidth, frame.PixelHeight);
            }
            catch { return default(System.Windows.Size); }
        });
        if (preview.Dispatcher.HasShutdownStarted) return;
        await preview.Dispatcher.InvokeAsync(() =>
        {
            if (version == preview._fileVersion) { preview._pixelSize = size; preview.UpdateVisual(); }
        });
    }

    public static readonly DependencyProperty ScreenSourceProperty =
        DependencyProperty.Register(nameof(ScreenSource), typeof(ImageSource), typeof(MonitorThumbnail),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ProfileNameProperty =
        DependencyProperty.Register(nameof(ProfileName), typeof(string), typeof(MonitorThumbnail),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(MonitorThumbnail),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty HasRulesProperty =
        DependencyProperty.Register(nameof(HasRules), typeof(bool), typeof(MonitorThumbnail),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty IsVideoProperty =
        DependencyProperty.Register(nameof(IsVideo), typeof(bool), typeof(MonitorThumbnail),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty ShowNameProperty =
        DependencyProperty.Register(nameof(ShowName), typeof(bool), typeof(MonitorThumbnail),
            new PropertyMetadata(true, OnChanged));

    public static readonly DependencyProperty BezelWidthProperty =
        DependencyProperty.Register(nameof(BezelWidth), typeof(double), typeof(MonitorThumbnail),
            new PropertyMetadata(120.0, OnSizeChanged));

    public static readonly DependencyProperty BezelHeightProperty =
        DependencyProperty.Register(nameof(BezelHeight), typeof(double), typeof(MonitorThumbnail),
            new PropertyMetadata(76.0, OnSizeChanged));

    public ImageSource? ScreenSource
    {
        get => (ImageSource?)GetValue(ScreenSourceProperty);
        set => SetValue(ScreenSourceProperty, value);
    }

    public string ProfileName
    {
        get => (string)GetValue(ProfileNameProperty);
        set => SetValue(ProfileNameProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool HasRules
    {
        get => (bool)GetValue(HasRulesProperty);
        set => SetValue(HasRulesProperty, value);
    }

    /// <summary>Shows the small play badge (live wallpaper source).</summary>
    public bool IsVideo
    {
        get => (bool)GetValue(IsVideoProperty);
        set => SetValue(IsVideoProperty, value);
    }

    public bool ShowName
    {
        get => (bool)GetValue(ShowNameProperty);
        set => SetValue(ShowNameProperty, value);
    }

    public double BezelWidth
    {
        get => (double)GetValue(BezelWidthProperty);
        set => SetValue(BezelWidthProperty, value);
    }

    public double BezelHeight
    {
        get => (double)GetValue(BezelHeightProperty);
        set => SetValue(BezelHeightProperty, value);
    }

    public MonitorThumbnail()
    {
        InitializeComponent();
        UpdateVisual();
    }

    public void Pulse()
    {
        if (ScreenSource == null)
        {
            return;
        }
        ScreenImage.Opacity = 0;
        var animation = new DoubleAnimation(0, ScreenOpacity, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        ScreenImage.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private static System.Windows.Media.Brush ThemeBrush(string key, System.Windows.Media.Brush fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush ?? fallback;

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MonitorThumbnail)d).UpdateVisual();

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MonitorThumbnail)d).UpdateVisual();

    private void UpdateVisual()
    {
        var accent = ThemeBrush("AccentBrush", AccentBrush);
        var inactive = ThemeBrush("BorderStrongBrush", InactiveBrush);

        Bezel.Width = BezelWidth;
        Bezel.Height = BezelHeight;
        Bezel.BorderBrush = IsActive ? accent : inactive;
        Bezel.SetResourceReference(Border.BorderBrushProperty, IsActive ? "AccentBrush" : "BorderStrongBrush");
        Bezel.BorderThickness = new Thickness(IsActive ? 3 : 1);
        Bezel.Effect = null;
        ScreenImage.Source = ScreenSource;
        ScreenImage.Stretch = PreviewFit switch { FitMode.Fit => Stretch.Uniform, FitMode.Stretch => Stretch.Fill,
            FitMode.Center => Stretch.None, _ => Stretch.UniformToFill };
        ScreenImage.Width = ScreenImage.Height = double.NaN;
        ScreenImage.HorizontalAlignment = HorizontalAlignment.Stretch; ScreenImage.VerticalAlignment = VerticalAlignment.Stretch;
        ScreenImage.Visibility = Visibility.Visible;
        ScreenSurface.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        if ((PreviewFit is FitMode.Center or FitMode.Tile) && ScreenSource is BitmapSource bitmap)
        {
            var pixels = _pixelSize.Width > 0 ? _pixelSize : new System.Windows.Size(bitmap.PixelWidth, bitmap.PixelHeight);
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            var width = pixels.Width * Math.Max(1, BezelWidth - 4) / screen.Width;
            var height = pixels.Height * Math.Max(1, BezelHeight - 4) / screen.Height;
            if (PreviewFit == FitMode.Center)
            {
                ScreenImage.Stretch = Stretch.Fill; ScreenImage.Width = width; ScreenImage.Height = height;
                ScreenImage.HorizontalAlignment = HorizontalAlignment.Center; ScreenImage.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                ScreenImage.Visibility = Visibility.Collapsed;
                ScreenSurface.Background = new ImageBrush(ScreenSource) { TileMode = TileMode.Tile, Stretch = Stretch.Fill,
                    ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, width, height) };
            }
        }
        EmptyState.Visibility = ScreenSource == null ? Visibility.Visible : Visibility.Collapsed;
        ScreenImage.Opacity = ScreenSource == null ? 1.0 : ScreenOpacity;
        SignalDot.Visibility = HasRules ? Visibility.Visible : Visibility.Collapsed;
        VideoBadge.Visibility = IsVideo && ScreenSource != null ? Visibility.Visible : Visibility.Collapsed;
        NameText.Visibility = ShowName ? Visibility.Visible : Visibility.Collapsed;
        NameText.Text = ProfileName;
    }
}

public sealed class SourceThumbnail : System.Windows.Controls.Image
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(nameof(FilePath), typeof(string),
        typeof(SourceThumbnail), new PropertyMetadata("", Changed));
    private int _version;
    public string FilePath { get => (string)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public SourceThumbnail() { Stretch = Stretch.UniformToFill; Width = 48; Height = 32; }
    private static async void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var image = (SourceThumbnail)d;
        var version = ++image._version;
        image.Source = null;
        try
        {
            var thumbnail = await Task.Run(() => ThumbnailLoader.Load(e.NewValue as string ?? "", 120));
            if (image.Dispatcher.HasShutdownStarted) return;
            await image.Dispatcher.InvokeAsync(() => { if (version == image._version) image.Source = thumbnail; });
        }
        catch (Exception ex) { WallpaperProfiles.Infrastructure.Logger.Error("Loading source thumbnail failed.", ex); }
    }
}
