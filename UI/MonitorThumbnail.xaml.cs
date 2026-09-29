using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WallpaperProfiles.UI;

public partial class MonitorThumbnail : System.Windows.Controls.UserControl
{
    private const double ScreenOpacity = 1.0;

    private static readonly SolidColorBrush AccentBrush = new(System.Windows.Media.Color.FromRgb(0x83, 0xAD, 0xB7));
    private static readonly SolidColorBrush InactiveBrush = new(System.Windows.Media.Color.FromRgb(0x41, 0x4C, 0x5A));

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
        Bezel.BorderThickness = new Thickness(IsActive ? 3 : 1);
        Bezel.Effect = null;
        ScreenImage.Source = ScreenSource;
        EmptyState.Visibility = ScreenSource == null ? Visibility.Visible : Visibility.Collapsed;
        ScreenImage.Opacity = ScreenSource == null ? 1.0 : ScreenOpacity;
        SignalDot.Visibility = HasRules ? Visibility.Visible : Visibility.Collapsed;
        VideoBadge.Visibility = IsVideo && ScreenSource != null ? Visibility.Visible : Visibility.Collapsed;
        NameText.Visibility = ShowName ? Visibility.Visible : Visibility.Collapsed;
        NameText.Text = ProfileName;
    }
}
