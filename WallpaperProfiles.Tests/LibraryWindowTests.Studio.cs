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
    private static void CheckWallpaperStudio(string dir, string imagePath)
    {
        var second = Path.Combine(dir, "city.png"); File.Copy(imagePath, second);
        var profile = new WallpaperProfile { Name = "Evening", FolderPath = imagePath, AdditionalWallpaperPaths = new() { second }, SlideshowRandom = true, SlideshowIntervalMinutes = 1 };
        var store = new DesktopCanvasStore(Path.Combine(dir, "studio.json"));
        var clock = new DesktopWidget { ProfileId = profile.Id, Kind = DesktopWidgetKind.Clock, FontSize = 76, Width = 520, Height = 310,
            Alignment = WidgetTextAlignment.Center, FontFamily = ClockWidgetView.ScriptFont, ShowHeader = false, ShowBackground = false, ShowBorder = false };
        var document = new DesktopCanvasDocument { Enabled = true, ActiveProfileId = profile.Id, ActiveWallpaperPath = imagePath, Widgets = new() { clock } };
        document.CustomizeWallpaper(profile.Id, second); store.Save(document);
        var studio = new DesktopCanvasView(store, () => { }, () => new[] { profile }, () => profile.Id) { ChooseDraftAction = () => DraftChoice.Discard };
        var window = new Window { Width = 1000, Height = 790, Content = studio, ShowInTaskbar = false, Opacity = 0 };
        window.Show(); studio.OpenWallpaper(profile.Id, second);
        var picker = (ComboBox)studio.FindName("WallpaperCanvasPicker");
        PumpUntil(() => picker.Items.Count == 3 && picker.SelectedIndex == 2);
        var list = (ListBox)studio.FindName("WidgetList"); Assert.Single(list.Items);
        list.SelectedIndex = 0;
        ((ComboBox)studio.FindName("ClockSkinPicker")).SelectedItem = ClockSkin.Stacked;
        ((ComboBox)studio.FindName("VisualizerSkinPicker")).SelectedItem = SpectrumSkin.Ring;
        ((TextBox)studio.FindName("WidgetPositionX")).Text = "520";
        ((Button)studio.FindName("SaveWidgetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var custom = store.Load().Composition(profile.Id, second, false).Single();
        Assert.Equal(ClockSkin.Stacked, custom.ClockSkin); Assert.Equal(520, custom.X); Assert.Equal(SpectrumSkin.Ring, custom.SpectrumSkin);
        Assert.Equal(ClockSkin.Script, store.Load().Composition(profile.Id, imagePath, false).Single().ClockSkin);
        var overview = (System.Windows.Controls.Canvas)typeof(DesktopCanvasView).GetField("_desktopOverview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(studio)!;
        var retainedItem = overview.Children.OfType<Border>().Single(item => item.Tag is DesktopWidget widget && widget.Id == custom.Id);
        var retainedContent = ((Viewbox)retainedItem.Child).Child;
        var savedBeforeDragging = File.ReadAllText(store.FilePath);
        var allocationsBefore = GC.GetAllocatedBytesForCurrentThread();
        var interaction = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) ((TextBox)studio.FindName("WidgetPositionX")).Text = (520 + i).ToString(CultureInfo.InvariantCulture);
        ((TextBox)studio.FindName("WidgetPositionX")).Text = "520";
        interaction.Stop();
        Assert.Same(retainedItem, overview.Children.OfType<Border>().Single(item => item.Tag is DesktopWidget widget && widget.Id == custom.Id));
        Assert.Same(retainedContent, ((Viewbox)retainedItem.Child).Child);
        Assert.Equal(savedBeforeDragging, File.ReadAllText(store.FilePath));
        var metricsDirectory = Environment.GetEnvironmentVariable("BELIE_WORKSPACE_PREVIEW");
        if (!string.IsNullOrEmpty(metricsDirectory))
            File.WriteAllText(Path.Combine(metricsDirectory, "preview-interactions.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Updates = 101, Milliseconds = interaction.Elapsed.TotalMilliseconds,
                AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationsBefore,
                VisualTreeRetained = true, SavedDataUnchanged = true
            }));
        RenderWorkspace(window, "wallpaper-studio", 1000, 790);
        RenderWorkspace(window, "wallpaper-studio-compact", 650, 600);
        picker.SelectedIndex = 1; Assert.Equal(clock.Id, Assert.IsType<DesktopWidget>(list.Items[0]).Id);
        ((Button)studio.FindName("CustomizeWallpaperLayoutButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(store.Load().HasLayout(profile.Id, imagePath));
        list.SelectedIndex = 0;
        ((TextBox)studio.FindName("WidgetTitle")).Text = "Pending";
        studio.ChooseDraftAction = () => DraftChoice.Cancel;
        picker.SelectedIndex = 2; Assert.Equal(1, picker.SelectedIndex); Assert.True(studio.IsDraftDirty);
        studio.ChooseDraftAction = () => DraftChoice.Discard;
        ((Button)studio.FindName("CloseWidgetEditorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((CheckBox)studio.FindName("FollowDesktopWallpaper")).IsChecked = true;
        store.Update(saved => saved.ActiveWallpaperPath = second);
        typeof(DesktopCanvasView).GetMethod("RefreshStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(studio, null);
        Assert.Equal(2, picker.SelectedIndex);
        Assert.Equal(custom.Id, Assert.IsType<DesktopWidget>(list.Items[0]).Id);
        // Exercise the real shuffle callback while the same profile stays active.
        var applied = new List<string>();
        using (var slideshow = new WallpaperProfiles.Engine.SlideshowController((path, _) =>
        {
            store.Update(saved => saved.ActiveWallpaperPath = path);
            Assert.Single(store.Load().VisibleWidgets()); applied.Add(path);
        }))
        {
            slideshow.Start(profile); PumpUntil(() => applied.Count > 0);
            var advance = typeof(WallpaperProfiles.Engine.SlideshowController).GetMethod("AdvanceAndApplyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (var i = 0; i < 4; i++)
            {
                var task = (Task)advance.Invoke(slideshow, null)!; PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult();
                Assert.NotEqual(applied[^2], applied[^1]);
            }
        }
        ((Button)studio.FindName("CloseWidgetEditorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var savedLayout = File.ReadAllText(store.FilePath);
        window.Width = 650; window.Height = 650; window.UpdateLayout();
        RenderWorkspace(window, "studio-gallery-compact", 650, 650);
        var previewFrame = (Border)typeof(DesktopCanvasView).GetField("_overviewFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(studio)!;
        var previewBottom = previewFrame.TransformToAncestor(studio).Transform(new System.Windows.Point(0, previewFrame.ActualHeight)).Y;
        var listTop = list.TransformToAncestor(studio).Transform(new System.Windows.Point(0, 0)).Y;
        Assert.True(listTop >= previewBottom, "The compact widget list must follow the preview without overlap.");
        Assert.True(previewFrame.ActualHeight >= 120, "The compact preview must remain usable.");
        var more = (Button)studio.FindName("WidgetActionsButton");
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach (var name in new[] { "OpenStyleCatalogueButton", "SaveWidgetLayoutButton", "BrowseWidgetLayoutsButton", "FindWidgetsButton", "QuickAddClockButton", "QuickAddVisualizerButton", "QuickAddNowPlayingButton" })
        {
            var action = (Button)studio.FindName(name);
            Assert.True(action.IsVisible, name + " must remain reachable in the overflow.");
            Assert.True(action.ActualHeight >= 36, name + " must retain a usable click target.");
        }
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(savedLayout, File.ReadAllText(store.FilePath));
        window.Close();
        var catalogue = new WidgetStyleCatalogWindow(store, imagePath) { Opacity = 0, ShowInTaskbar = false };
        catalogue.Show(); RenderWorkspace(catalogue, "clock-style-catalogue", 880, 720);
        VisualChildren(catalogue).OfType<Button>().Single(button => Equals(button.Content, "Visualizers")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RenderWorkspace(catalogue, "visualizer-style-catalogue", 880, 720);
        Assert.Equal(8, VisualChildren(catalogue).OfType<AudioSpectrumView>().Count());
        catalogue.Close();
        // Every clock skin must survive changing skins on the same live view.
        var view = new ClockWidgetView(clock);
        foreach (var skin in Enum.GetValues<ClockSkin>())
        {
            clock.ClockSkin = skin; view.UpdateAppearance(clock);
            view.Measure(new Size(520, 400)); view.Arrange(new Rect(0, 0, 520, 400));
            new RenderTargetBitmap(520, 400, 96, 96, PixelFormats.Pbgra32).Render(view);
        }
    }

    private static void CheckWidgetDrafts(MainWindow main)
    {
        var view = (DesktopCanvasView)((ContentControl)main.FindName("CanvasPane")).Content;
        Click(main, "CanvasNavButton");
        var list = (ListBox)view.FindName("WidgetList");
        list.SelectedIndex = 0;
        var title = (TextBox)view.FindName("WidgetTitle");
        var original = title.Text;
        title.Text = "Pending widget title";
        var scope = (ComboBox)view.FindName("WidgetScopeFilter");
        var originalScope = scope.SelectedItem;
        view.ChooseDraftAction = () => DraftChoice.Cancel;
        scope.SelectedIndex = 1;
        Assert.Equal(originalScope, scope.SelectedItem);
        Assert.Equal("Pending widget title", title.Text);
        Assert.True(view.IsDraftDirty);
        ((Button)view.FindName("CloseWidgetEditorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.NotNull(list.SelectedItem);
        view.ChooseDraftAction = () => DraftChoice.Save;
        Assert.True(view.TryLeaveDraft());
        Assert.False(view.IsDraftDirty);
        title.Text = original;
        ((Button)view.FindName("SaveWidgetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        title.Text = "Discard this widget title";
        view.ChooseDraftAction = () => DraftChoice.Discard;
        Assert.True(view.TryLeaveDraft());
        Assert.Equal(original, title.Text);
        view.ConfirmDeleteAction = () => false;
        var count = list.Items.Count;
        ((Button)view.FindName("DeleteWidgetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(count, list.Items.Count);
        ((Button)view.FindName("CloseWidgetEditorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(list.SelectedItem);
    }
}
