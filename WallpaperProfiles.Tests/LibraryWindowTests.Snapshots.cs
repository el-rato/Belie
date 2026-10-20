using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Net.Http;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed partial class LibraryWindowTests
{
    private static void CaptureAuditViews(MainWindow main, Guid profileId)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BELIE_WORKSPACE_PREVIEW"))) return;
        foreach (var theme in new[] { "Ember", "Daylight" })
        {
            ((ListBox)main.FindName("ThemePicker")).SelectedItem = UiAppearance.Find(theme);
            typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profileId });
            var tabs = (TabControl)main.FindName("ProfileTabs");
            for (var index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index;
                RenderWorkspace(main, $"audit-{theme.ToLowerInvariant()}-profile-{index}", 1180, 820);
                RenderWorkspace(main, $"audit-{theme.ToLowerInvariant()}-profile-{index}-compact", 880, 650);
            }
            tabs.SelectedIndex = 0;
            Click(main, "DashboardNavButton"); RenderWorkspace(main, $"audit-{theme.ToLowerInvariant()}-overview", 1180, 820);
            Click(main, "LibraryNavButton"); RenderWorkspace(main, $"audit-{theme.ToLowerInvariant()}-library", 1180, 820);
            var library = (LibraryWindow)typeof(MainWindow).GetField("_libraryView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            var gallery = (ListBox)library.FindName("Gallery");
            gallery.SelectedIndex = 0;
            RenderWorkspace(main, $"audit-{theme.ToLowerInvariant()}-library-details", 640, 650);
            Assert.Equal(Visibility.Visible, ((Button)library.FindName("BackToCollectionButton")).Visibility);
            Click(library, "BackToCollectionButton");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)library.FindName("LocalGalleryFrame")).Visibility);
            Click(main, "CanvasNavButton"); RenderWorkspace(main, $"audit-{theme.ToLowerInvariant()}-widgets", 1180, 820);
            var widgets = (DesktopCanvasView)((ContentControl)main.FindName("CanvasPane")).Content;
            ((ListBox)widgets.FindName("WidgetList")).SelectedIndex = 0;
            foreach (var tab in new[] { "Content", "Style", "Layout" })
            {
                ((Button)widgets.FindName("Widget" + tab + "Tab")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RenderWorkspace(main, $"audit-{theme.ToLowerInvariant()}-widget-{tab.ToLowerInvariant()}", 880, 650);
            }
            ((Button)widgets.FindName("CloseWidgetEditorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click(main, "SettingsButton");
            var settings = System.Windows.Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Belie settings");
            RenderWorkspace(settings, $"audit-{theme.ToLowerInvariant()}-settings", 400, 430);
            Click(main, "SettingsButton");
            foreach (var key in new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "AccentTextBrush" })
            {
                var ink = AccentInkConverter.Luminance(((SolidColorBrush)main.FindResource(key)).Color);
                foreach (var background in new[] { "BgBrush", "SurfaceBrush", "SurfaceRaisedBrush" })
                {
                    var paper = AccentInkConverter.Luminance(((SolidColorBrush)main.FindResource(background)).Color);
                    Assert.True((Math.Max(ink, paper) + .05) / (Math.Min(ink, paper) + .05) >= 4.5, key + " contrast in " + theme);
                }
            }
        }
        ((ListBox)main.FindName("ThemePicker")).SelectedItem = UiAppearance.Find("Sage");
        CaptureFitViews();
    }

    private static void CaptureFitViews()
    {
        foreach (var (name, width, height) in new[] { ("portrait", 300, 900), ("ultrawide", 1600, 450) })
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.Teal, null, new Rect(0, 0, width, height));
                drawing.DrawRectangle(Brushes.Goldenrod, null, new Rect(0, 0, width / 3d, height));
                drawing.DrawRectangle(Brushes.Coral, null, new Rect(width * 2 / 3d, 0, width / 3d, height));
            }
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual); image.Freeze();
            var monitor = new MonitorThumbnail { ScreenSource = image, ShowName = false, BezelWidth = 400, BezelHeight = 225 };
            var window = new Window { Content = monitor, Width = 440, Height = 265 };
            foreach (var fit in Enum.GetValues<FitMode>())
            {
                monitor.PreviewFit = fit;
                RenderWorkspace(window, $"audit-preview-{name}-{fit}", 440, 265);
            }
            RenderWorkspace(window, $"audit-preview-{name}-150dpi", 440, 265, 144);
            RenderWorkspace(window, $"audit-preview-{name}-200dpi", 440, 265, 192);
            window.Close();
        }
    }
}
