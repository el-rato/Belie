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
    private static async Task CheckMarketplaceAsync(LibraryWindow window, LibraryStore store, MarketplaceHandler handler)
    {
        var gallery = (ListBox)window.FindName("MarketGallery");
        var search = (TextBox)window.FindName("MarketSearchBox");
        var status = (TextBlock)window.FindName("MarketStatusText");
        bool Loading() => (bool)typeof(LibraryWindow).GetField("_marketLoading", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Click(window, "MarketplaceTab");
        await WaitUntil(() => !Loading());
        Assert.Single(gallery.Items.Cast<object>());
        Assert.Contains(handler.Requests, r => r.Contains("sorting=toplist") && r.Contains("topRange=1M"));
        var landscape = (CheckBox)window.FindName("MarketLandscapeOnly");
        search.Text = "orientations";
        landscape.IsChecked = true;
        await WaitUntil(() => !Loading());
        Assert.Equal(3, gallery.Items.Count);
        Assert.Contains("ratios=landscape", handler.Requests.Last(r => r.Contains("/search")));
        landscape.IsChecked = false;
        await WaitUntil(() => !Loading());
        Assert.Equal(5, gallery.Items.Count);
        Assert.DoesNotContain("ratios=", handler.Requests.Last(r => r.Contains("/search")));
        search.Text = "";
        Click(window, "MarketSearchButton");
        await WaitUntil(() => !Loading());
        Click(window, "MarketNewestSortButton");
        await WaitUntil(() => !Loading());
        Assert.Contains("sorting=date_added", handler.Requests.Last(r => r.Contains("/search")));
        Click(window, "MarketTopSortButton");
        await WaitUntil(() => !Loading());
        Assert.Contains("sorting=toplist", handler.Requests.Last(r => r.Contains("/search")));
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("LocalGalleryPane")).Visibility);
        Click(window, "MarketLoadMoreButton");
        await WaitUntil(() => !Loading());
        Assert.Equal(2, gallery.Items.Count); // Duplicate IDs across pages are skipped.
        Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("MarketLoadMoreButton")).Visibility);
        gallery.SelectedIndex = 0;
        await WaitUntil(() => ((Button)window.FindName("MarketDownloadButton")).IsEnabled);
        await WaitUntil(() => ((TextBlock)window.FindName("MarketPreviewStatus")).Text.Length == 0);
        window.UpdateLayout();
        var previewButton = (FrameworkElement)window.FindName("MarketExpandPreviewButton");
        Assert.True(previewButton.ActualWidth > gallery.ActualWidth * 2);
        var firstThumbnail = (FrameworkElement)gallery.ItemContainerGenerator.ContainerFromIndex(0);
        var secondThumbnail = (FrameworkElement)gallery.ItemContainerGenerator.ContainerFromIndex(1);
        Assert.Equal(firstThumbnail.TranslatePoint(new System.Windows.Point(), gallery).X,
            secondThumbnail.TranslatePoint(new System.Windows.Point(), gallery).X);
        RenderWorkspace(window, "marketplace-focus", 1000, 720);
        window.Width = 900; window.Height = 650; window.UpdateLayout();
        var compactDetails = (FrameworkElement)window.FindName("MarketDetails");
        var compactDownload = (FrameworkElement)window.FindName("MarketDownloadButton");
        Assert.InRange(compactDownload.TranslatePoint(new System.Windows.Point(), compactDetails).Y + compactDownload.ActualHeight,
            1, compactDetails.ActualHeight);
        RenderWorkspace(window, "marketplace-focus-compact", 900, 650);
        window.Width = 1000; window.Height = 720; window.UpdateLayout();
        Click(window, "MarketBackToGalleryButton");
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("MarketDetails")).Visibility);
        Assert.Equal(2, gallery.Items.Count);

        search.Text = "slow";
        Click(window, "MarketSearchButton");
        search.Text = "nature";
        Click(window, "MarketSearchButton");
        await WaitUntil(() => !Loading());
        Assert.Single(gallery.Items.Cast<object>());
        gallery.SelectedIndex = 0;
        await WaitUntil(() => ((Button)window.FindName("MarketDownloadButton")).IsEnabled);
        var tags = (ItemsControl)window.FindName("MarketTags");
        Assert.Equal("nature", Assert.IsType<WallhavenTag>(tags.Items[0]).Name);
        window.UpdateLayout();
        Assert.Same(((ListBox)window.FindName("Gallery")).ItemContainerStyle, gallery.ItemContainerStyle);
        Click(window, "MarketTagsButton");
        window.UpdateLayout();
        var tagsPopup = (System.Windows.Controls.Primitives.Popup)window.FindName("MarketTagsPopup");
        Assert.True(tagsPopup.IsOpen);
        var tagButton = VisualChildren(tags).OfType<Button>().First();
        tagButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntil(() => !Loading());
        Assert.Equal("id:42", search.Text);
        Assert.Contains(handler.Requests, r => r.Contains("q=id%3A42"));
        gallery.SelectedIndex = 0;
        await WaitUntil(() => ((Button)window.FindName("MarketDownloadButton")).IsEnabled);
        CheckLargeMarketplacePreview(window);
        Assert.False(tagsPopup.IsOpen);
        var originalTags = tags.ItemsSource;
        window.UpdateLayout();
        var previewHeight = previewButton.ActualHeight;
        tags.ItemsSource = Enumerable.Range(1, 30).Select(i => new WallhavenTag(i, "Wallpaper tag " + i)).ToArray();
        window.UpdateLayout();
        Assert.Equal(previewHeight, previewButton.ActualHeight, precision: 1);
        Click(window, "MarketTagsButton");
        window.UpdateLayout();
        Assert.True(tagsPopup.IsOpen);
        Assert.Equal(30, VisualChildren(tags).OfType<Button>().Count());
        Assert.Equal(previewHeight, previewButton.ActualHeight, precision: 1);
        var tagScroll = (ScrollViewer)window.FindName("MarketTagsScroll");
        Assert.InRange(tagScroll.ActualHeight, 1, 180);
        tagScroll.ScrollToEnd();
        Click(window, "MarketTagsButton");
        Assert.False(tagsPopup.IsOpen);
        RenderWorkspace(window, "marketplace-many-tags", 1000, 720);
        var detailsPanel = (FrameworkElement)window.FindName("MarketDetails");
        var downloadButton = (Button)window.FindName("MarketDownloadButton");
        var downloadPosition = downloadButton.TranslatePoint(new System.Windows.Point(), detailsPanel);
        Assert.InRange(downloadPosition.Y + downloadButton.ActualHeight, 1, detailsPanel.ActualHeight);
        tags.ItemsSource = originalTags;
        Click(window, "MarketDownloadButton");
        await WaitUntil(() => ((Button)window.FindName("MarketUseButton")).Visibility == Visibility.Visible);
        await WaitUntil(() => ((Button)window.FindName("LocalLibraryTab")).IsEnabled);
        var asset = Assert.Single(store.Load(), a => a.Collection == "Wallhaven");
        Assert.True(File.Exists(asset.FilePath));
        Assert.Equal(new[] { "nature" }, asset.Tags);
        Assert.Equal(Visibility.Visible, ((Button)window.FindName("MarketUseButton")).Visibility);
        WallpaperAsset? chosen = null;
        var selection = typeof(LibraryWindow).GetField("_inlineSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
        selection.SetValue(window, new Action<WallpaperAsset>(value => chosen = value));
        Click(window, "MarketUseButton");
        selection.SetValue(window, null);
        Assert.Equal(asset.FilePath, chosen?.FilePath);
        RenderWorkspace(window, "marketplace", 1000, 720);

        search.Text = "rate";
        Click(window, "MarketSearchButton");
        await WaitUntil(() => !Loading());
        Assert.Contains("Wait a minute", status.Text);
        search.Text = "nature";
        Click(window, "MarketSearchButton");
        await WaitUntil(() => !Loading());
        Assert.Single(gallery.Items.Cast<object>());
        Click(window, "LocalLibraryTab");
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("LocalGalleryPane")).Visibility);
    }

    private static void CheckLargeMarketplacePreview(LibraryWindow library)
    {
        Exception? failure = null;
        library.Dispatcher.BeginInvoke(new Action(async () =>
        {
            var preview = System.Windows.Application.Current.Windows.OfType<Window>()
                .Single(w => w.Title.EndsWith(" · Preview", StringComparison.Ordinal));
            try
            {
                await WaitUntil(() => VisualChildren(preview).OfType<TextBlock>().Any(t => t.Text.Contains("Full-size preview")));
                preview.UpdateLayout();
                var image = VisualChildren(preview).OfType<System.Windows.Controls.Image>().Single();
                Assert.True(image.ActualWidth > 600, $"Preview width: {image.ActualWidth}; window: {preview.ActualWidth}");
                Assert.True(image.ActualHeight > 300, $"Preview height: {image.ActualHeight}; window: {preview.ActualHeight}");
                Assert.Equal(WindowState.Maximized, preview.WindowState);
                var previewContent = (FrameworkElement)preview.Content;
                RenderWorkspace(preview, "marketplace-expanded", (int)previewContent.ActualWidth, (int)previewContent.ActualHeight);
                VisualChildren(preview).OfType<Button>().Single(b => b.Content is string text && text.StartsWith("Back to marketplace"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { failure = ex; preview.Close(); }
        }));
        Click(library, "MarketExpandPreviewButton");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
