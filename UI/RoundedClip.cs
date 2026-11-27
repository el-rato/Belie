using System.Windows;
using System.Windows.Media;

namespace WallpaperProfiles.UI;

/// <summary>Clips an element to a rounded rectangle that follows its size, so content stays inside a pane's corners.</summary>
internal static class RoundedClip
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached("Radius", typeof(double), typeof(RoundedClip),
        new PropertyMetadata(0d, (target, e) =>
        {
            if (target is not FrameworkElement element) return;
            element.SizeChanged -= Update;
            if ((double)e.NewValue > 0) element.SizeChanged += Update;
            Apply(element);
        }));

    public static double GetRadius(DependencyObject target) => (double)target.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject target, double value) => target.SetValue(RadiusProperty, value);

    private static void Update(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement element)
    {
        var radius = GetRadius(element);
        if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0) { element.Clip = null; return; }
        var clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        clip.Freeze();
        element.Clip = clip;
    }
}
