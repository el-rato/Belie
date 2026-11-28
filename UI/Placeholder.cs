using System.Windows;

namespace WallpaperProfiles.UI;

/// <summary>
/// Grey text shown inside an empty text box, saying what goes there. The theme's text box draws it; it disappears as
/// soon as there is text.
/// </summary>
internal static class Placeholder
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Placeholder), new FrameworkPropertyMetadata(""));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
}
