using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

/// <summary>Draws the bar's Team panel to a picture. Does nothing unless BELIE_TEAM_RENDER names the file to write.</summary>
public class TeamPanelRenderTests
{
    [Fact]
    public void DrawsThePanel()
    {
        var file = Environment.GetEnvironmentVariable("BELIE_TEAM_RENDER");
        if (string.IsNullOrEmpty(file)) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new TeamPanel(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF2)), new SolidColorBrush(Color.FromRgb(0x3F, 0xB5, 0xC8)))
                    { Width = TeamPanel.PanelWidth - 32 };
                var card = new System.Windows.Controls.Border { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1C)), CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Child = panel };
                card.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                card.Measure(new Size(TeamPanel.PanelWidth, double.PositiveInfinity));
                card.Arrange(new Rect(card.DesiredSize));
                card.UpdateLayout();
                var picture = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                picture.Render(card);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(picture));
                using var stream = File.Create(file);
                png.Save(stream);
                Assert.True(card.ActualHeight <= TeamPanel.PanelHeight, $"The panel is {card.ActualHeight:0} high, more than the popup's {TeamPanel.PanelHeight}.");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }
}
