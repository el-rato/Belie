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
    private static void CheckNowPlayingWidget(MainWindow main, string dir)
    {
        Click(main, "CanvasNavButton");
        var view = (DesktopCanvasView)((ContentControl)main.FindName("CanvasPane")).Content;
        void Press(string name) => ((Button)view.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Press("AddNowPlayingButton");
        Assert.Equal(Visibility.Collapsed, ((TextBox)view.FindName("WidgetContent")).Visibility);
        Press("SaveWidgetButton");
        var store = new DesktopCanvasStore(Path.Combine(dir, "canvas.json"));
        var saved = Assert.Single(store.Load().Widgets, widget => widget.Kind == DesktopWidgetKind.NowPlaying);
        Assert.False(saved.ShowBorder); Assert.False(saved.ShowBackground); Assert.False(saved.ShowHeader);
        ((ComboBox)view.FindName("WidgetMusicLayout")).SelectedItem = NowPlayingLayout.Stacked;
        ((CheckBox)view.FindName("WidgetAlbumArt")).IsChecked = false;
        ((TextBox)view.FindName("WidgetMusicAccent")).Text = "#FF8855";
        var spectrumOption = (CheckBox)view.FindName("WidgetAudioVisualizer");
        Assert.True(spectrumOption.IsVisible);
        spectrumOption.IsChecked = true;
        Assert.True(view.IsDraftDirty);
        Press("SaveWidgetButton");
        saved = store.Load().Widgets.Single(widget => widget.Id == saved.Id);
        Assert.Equal(NowPlayingLayout.Stacked, saved.MusicLayout);
        Assert.True(saved.ShowAudioVisualizer);
        Assert.True(saved.Height >= 380);
        Assert.False(saved.ShowAlbumArt); Assert.Equal("#FF8855", saved.MusicAccentColor);
        RenderWorkspace(main, "now-playing-editor", 1040, 790);
        Press("DuplicateWidgetButton");
        var widgets = store.Load().Widgets.Where(widget => widget.Kind == DesktopWidgetKind.NowPlaying).ToArray();
        Assert.Equal(2, widgets.Length);
        Assert.All(widgets, widget => { Assert.False(widget.ShowBorder); Assert.Equal("#FF8855", widget.MusicAccentColor); });
        CheckNowPlayingDrag(store, saved);
        Press("StyleRainmeterButton");
        Press("SaveWidgetButton");
        var minimal = store.Load().Widgets.Last(widget => widget.Kind == DesktopWidgetKind.NowPlaying);
        Assert.Equal(NowPlayingLayout.Minimal, minimal.MusicLayout);
        Assert.False(minimal.ShowAlbumArt); Assert.False(minimal.ShowPlaybackControls);
        Assert.True(minimal.ShowPlaybackProgress);
        Assert.False(minimal.ShowHeader); Assert.False(minimal.ShowBorder); Assert.False(minimal.ShowBackground);
        RenderWorkspace(main, "now-playing-rainmeter", 1040, 790);
        Press("CloseWidgetEditorButton");
    }

    private static void CheckNowPlayingDrag(DesktopCanvasStore store, DesktopWidget saved)
    {
        saved.X = 200; saved.Y = 200;
        store.UpdateLayout(saved.Id, saved.X, saved.Y, saved.Width, saved.Height);
        var pointer = new System.Drawing.Point(500, 500);
        var window = new DesktopWidgetWindow(saved, store, () => pointer) { Opacity = 0 };
        window.Show(); window.UpdateLayout();
        var card = (Border)window.Content;
        var song = VisualChildren(card).OfType<TextBlock>().First(t => t.ToolTip is string);
        var start = store.Load().Widgets.Single(widget => widget.Id == saved.Id);
        try
        {
            card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = song });
            pointer = new System.Drawing.Point(590, 545);
            card.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            var moved = store.Load().Widgets.Single(widget => widget.Id == saved.Id);
            Assert.Equal(start.X + 90, moved.X); Assert.Equal(start.Y + 45, moved.Y);
            var play = VisualChildren(card).OfType<Button>().First();
            card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = play });
            Assert.False(card.IsMouseCaptured);
            moved.Locked = true; window.Update(moved);
            card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = song });
            Assert.False(card.IsMouseCaptured);
        }
        finally { window.Close(); }
    }
}
