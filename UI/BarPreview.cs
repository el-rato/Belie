using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using WallpaperProfiles.Workspaces;

namespace WallpaperProfiles.UI;

/// <summary>Draws a look as the bar itself would, with made-up readings.</summary>
internal static class BarPreview
{
    public const double Width = 1180;

    public static BitmapSource Render(TopBarSettings settings, WallpaperColors? colors, ImageSource? wallpaper, double width = Width)
    {
        var window = new TopBarWindow("preview") { IsPreview = true };
        try
        {
            var sample = System.Text.Json.JsonSerializer.Deserialize<TopBarSettings>(System.Text.Json.JsonSerializer.Serialize(settings, Persistence.JsonOptions.Shared), Persistence.JsonOptions.Shared)!;
            sample.Css = settings.Css; sample.WorkspaceCount = 5; sample.HideEmptyWorkspaces = false; sample.WorkspacesEnabled = true;
            window.Apply(sample.Normalize(), colors);
            window.UpdateWorkspaces(Enumerable.Range(1, 5).Select(index => new WorkspaceState(index, index == 2, index is 1 or 2 or 4 ? 1 : 0)).ToArray());
            window.UpdateTitle("Notes"); window.UpdateClock(DateTime.Today.AddHours(21).AddMinutes(41));
            window.UpdateSystem(new SystemSnapshot(14, 47, 62, false, 86, false, 58, 1_800_000, 90_000, TimeSpan.FromHours(5)));
            window.UpdateWeather(new WeatherReport("", 21, 20, 56, 12, 2, true, false, Array.Empty<WeatherDay>()));
            window.UpdateMedia(new WidgetStyles.SamplePlayer().ReadAsync(default).Result);
            foreach (var script in sample.Modules.Where(module => module.Kind == BarModuleKind.Script)) window.UpdateScript(script, "Script", "");
            var bar = (FrameworkElement)window.Content;
            window.Content = null;
            var height = sample.Height + sample.BarMargin * 2 + 22;
            var scene = new Grid { Width = width, Height = height, ClipToBounds = true };
            scene.Background = wallpaper != null
                ? new ImageBrush(wallpaper) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Viewbox = new Rect(0, 0, 1, .5) }
                : new LinearGradientBrush(System.Windows.Media.Color.FromRgb(58, 68, 92), System.Windows.Media.Color.FromRgb(150, 110, 120), 20);
            scene.Children.Add(bar);
            scene.Measure(new System.Windows.Size(width, height)); scene.Arrange(new Rect(0, 0, width, height)); scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)(width * 1.25), (int)(height * 1.25), 120, 120, PixelFormats.Pbgra32);
            bitmap.Render(scene);
            bitmap.Freeze();
            scene.Children.Clear();
            return bitmap;
        }
        finally { window.Close(); }
    }
}
