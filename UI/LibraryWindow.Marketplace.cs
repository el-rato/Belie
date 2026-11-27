using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using Binding = System.Windows.Data.Binding;
using Panel = System.Windows.Controls.Panel;

namespace WallpaperProfiles.UI;

internal partial class LibraryWindow
{
    private readonly WallhavenClient _marketClient;
    private readonly string _marketDownloadFolder;
    private readonly ObservableCollection<MarketEntry> _marketEntries = new();
    private readonly Dictionary<string, WallhavenWallpaper> _marketMetadata = new();
    private CancellationTokenSource? _marketSearchCancellation;
    private CancellationTokenSource? _marketDetailCancellation;
    private CancellationTokenSource? _marketDownloadCancellation;
    private bool _marketInitialized;
    private bool _marketLoading;
    private bool _marketDownloading;
    internal bool IsWorking => _busy || _marketDownloading;
    private int _marketPage;
    private int _marketLastPage;
    private int _marketTotal;
    private string _marketQuery = "";
    private string _marketSorting = "toplist";
    private string _marketMinimumResolution = "";
    private bool _marketLandscapeOnly;
    private string _marketPurity = WallhavenClient.SafeOnly;
    private string _marketAppliedPurity = WallhavenClient.SafeOnly;
    private WallhavenWallpaper? _marketSelected;
    private WallpaperAsset? _marketDownloadedAsset;
    private Window? _marketPreviewWindow;
    private bool _marketPreviewIsFullSize;

    private sealed class MarketEntry : INotifyPropertyChanged
    {
        public WallhavenWallpaper Wallpaper { get; }
        public ImageSource? Thumbnail { get; private set; }
        public string PreviewStatus { get; private set; } = "Loading preview…";
        public event PropertyChangedEventHandler? PropertyChanged;
        public MarketEntry(WallhavenWallpaper wallpaper) => Wallpaper = wallpaper;
        public void SetThumbnail(ImageSource? image)
        {
            Thumbnail = image;
            PreviewStatus = image == null ? "Preview unavailable" : "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewStatus)));
        }
    }

    private void InitializeMarketplace()
    {
        MarketGallery.ItemsSource = _marketEntries;
        _marketEntries.CollectionChanged += (_, _) => UpdateMarketSteps();
        LibraryRoot.PreviewKeyDown += MarketViewer_KeyDown;
        LibraryRoot.Unloaded += (_, _) => CancelMarketRequests();
        Closed += (_, _) => CancelMarketRequests();
        LibraryRoot.Loaded += async (_, _) =>
        {
            if (MarketPane.Visibility == Visibility.Visible && !_marketInitialized && !_marketDownloading)
                await SearchMarketplaceAsync();
        };
    }

    private void CancelMarketRequests()
    {
        _marketPreviewWindow?.Close();
        _marketSearchCancellation?.Cancel();
        _marketDetailCancellation?.Cancel();
        _marketDownloadCancellation?.Cancel();
    }

    private void LocalLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _marketDownloading) return;
        CancelMarketRequests();
        MarketPane.Visibility = MarketFilters.Visibility = Visibility.Collapsed;
        LocalFilters.Visibility = LocalGalleryPane.Visibility = LocalFooter.Visibility = ImportActions.Visibility = Visibility.Visible;
        PaneHeading.Text = "Library";
        LocalLibraryTab.SetResourceReference(Control.BackgroundProperty, "AccentSubtleBrush");
        MarketplaceTab.ClearValue(Control.BackgroundProperty);
        RefreshGallery();
    }

    private async void Marketplace_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        LocalFilters.Visibility = LocalGalleryPane.Visibility = LocalFooter.Visibility = ImportActions.Visibility = Visibility.Collapsed;
        PaneHeading.Text = "Discover";
        MarketPane.Visibility = MarketFilters.Visibility = Visibility.Visible;
        MarketplaceTab.SetResourceReference(Control.BackgroundProperty, "AccentSubtleBrush");
        LocalLibraryTab.ClearValue(Control.BackgroundProperty);
        // A canceled request is safe to resume by starting the search again.
        if (!_marketInitialized || _marketSearchCancellation?.IsCancellationRequested == true)
            await SearchMarketplaceAsync();
    }

    private async void MarketSearch_Click(object sender, RoutedEventArgs e) => await SearchMarketplaceAsync();
    private async void MarketSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchMarketplaceAsync();
    }

    private async void MarketFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_marketInitialized && MarketPane.Visibility == Visibility.Visible)
            await SearchMarketplaceAsync();
    }

    private async void MarketOrientation_Changed(object sender, RoutedEventArgs e)
    {
        if (_marketInitialized && MarketPane.Visibility == Visibility.Visible)
            await SearchMarketplaceAsync();
    }

    private async void MarketPurity_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MarketPurity?.SelectedItem is not ComboBoxItem { Tag: string purity } || purity == _marketPurity) return;
        if (WallhavenClient.IncludesNsfw(purity) && !WallhavenClient.IsValidApiKey(_marketClient.ApiKey) && !AskWallhavenKey())
        {
            // No key, no NSFW: go back to the filter that was in use.
            MarketPurity.SelectedItem = MarketPurity.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == _marketPurity);
            return;
        }
        _marketPurity = purity;
        if (_marketInitialized && MarketPane.Visibility == Visibility.Visible)
            await SearchMarketplaceAsync();
    }

    private void MarketApiKey_Click(object sender, RoutedEventArgs e)
    {
        AskWallhavenKey();
        // The key was removed while NSFW results were on: fall back to safe results.
        if (!WallhavenClient.IsValidApiKey(_marketClient.ApiKey) && WallhavenClient.IncludesNsfw(_marketPurity))
            MarketPurity.SelectedIndex = 0;
    }

    /// <summary>Test hook that replaces the key dialog.</summary>
    internal Func<bool>? AskWallhavenKeyAction { get; set; }

    /// <summary>Asks for the person's own Wallhaven API key. Returns true when a key is saved.</summary>
    private bool AskWallhavenKey()
    {
        if (AskWallhavenKeyAction != null) return AskWallhavenKeyAction();
        var hasKey = WallhavenClient.IsValidApiKey(_marketClient.ApiKey);
        // The library is usually shown inside the main window, so own the dialog from whichever window hosts it.
        var dialog = new Window { Title = "Wallhaven API key", Owner = DialogOwner, Width = 460, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = "Wallhaven only shows NSFW wallpapers to signed-in adults. Paste the API key from your Wallhaven account to include them in Discover. "
                + "Belie keeps the key encrypted for your Windows account and sends it only to wallhaven.cc." });
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Find your key: wallhaven.cc → Settings → Account"))
            { NavigateUri = new Uri("https://wallhaven.cc/settings/account") };
        link.RequestNavigate += (_, args) =>
        {
            try { Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn("Could not open the Wallhaven settings page: " + ex.Message); }
        };
        panel.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 0, 0, 14) });
        var input = new PasswordBox { Margin = new Thickness(0, 0, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetName(input, "Wallhaven API key");
        panel.Children.Add(input);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            Text = hasKey ? "A key is saved. Paste a new one to replace it." : "" };
        error.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var remove = new Button { Content = "Remove key", Margin = new Thickness(0, 0, 8, 0), Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed };
        var save = new Button { Content = "Save", IsDefault = true, IsEnabled = false };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        input.PasswordChanged += (_, _) => save.IsEnabled = input.Password.Trim().Length > 0;
        save.Click += (_, _) =>
        {
            var key = input.Password.Trim();
            if (!WallhavenClient.IsValidApiKey(key)) { error.Text = "That doesn't look like a Wallhaven API key. Copy it again from your account settings."; return; }
            try { _wallhavenKeys.Save(key); }
            catch (Exception ex) { error.Text = "Could not save the key: " + ex.Message; return; }
            _marketClient.ApiKey = key;
            dialog.DialogResult = true;
        };
        remove.Click += (_, _) =>
        {
            try { _wallhavenKeys.Clear(); }
            catch (Exception ex) { error.Text = "Could not remove the key: " + ex.Message; return; }
            _marketClient.ApiKey = "";
            dialog.DialogResult = false;
        };
        buttons.Children.Add(remove); buttons.Children.Add(save); buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        UiAppearance.SetDialogContent(dialog, panel, "Wallhaven API key");
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true;
    }

    private async void MarketSort_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _marketDownloading || sender is not Button { Tag: string sorting } selected || sorting == _marketSorting) return;
        _marketSorting = sorting;
        foreach (var button in MarketSortBar.Children.OfType<Button>())
        {
            button.ClearValue(Control.BackgroundProperty);
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, button == selected ? "Selected" : "");
        }
        selected.SetResourceReference(Control.BackgroundProperty, "AccentSubtleBrush");
        await SearchMarketplaceAsync();
    }

    private async Task SearchMarketplaceAsync()
    {
        if (_marketDownloading || _busy || _closed) return;
        _marketSearchCancellation?.Cancel();
        _marketSearchCancellation?.Dispose();
        _marketSearchCancellation = new CancellationTokenSource();
        _marketDetailCancellation?.Cancel();
        _marketQuery = MarketSearchBox.Text.Trim();
        _marketMinimumResolution = (MarketResolution.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        _marketLandscapeOnly = MarketLandscapeOnly.IsChecked == true;
        _marketAppliedPurity = _marketPurity;
        _marketPage = _marketLastPage = 0;
        _marketInitialized = false;
        _marketEntries.Clear();
        _marketMetadata.Clear();
        MarketGallery.UnselectAll();
        ShowMarketSelection(null);
        if (VisualTreeHelper.GetChildrenCount(MarketGallery) > 0)
            FindMarketScrollViewer(MarketGallery)?.ScrollToTop();
        await LoadMarketPageAsync(_marketSearchCancellation, firstPage: true);
    }

    private async Task LoadMarketPageAsync(CancellationTokenSource request, bool firstPage = false)
    {
        if (!firstPage && (_marketLoading || _marketDownloading || _marketPage >= _marketLastPage)) return;
        _marketLoading = true;
        MarketDownloadButton.IsEnabled = false;
        MarketLoadMoreButton.IsEnabled = false;
        MarketStatusText.Text = firstPage ? "Searching Wallhaven…" : "Loading more wallpapers…";
        MarketEmptyText.Text = "Finding wallpapers…";
        MarketEmptyText.Visibility = _marketEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MarketProgress.IsIndeterminate = true;
        MarketProgress.Visibility = Visibility.Visible;
        var token = request.Token;
        try
        {
            var page = await _marketClient.SearchAsync(_marketQuery, _marketSorting, _marketMinimumResolution, _marketPage + 1, token, _marketLandscapeOnly, _marketAppliedPurity);
            if (token.IsCancellationRequested || _closed || request != _marketSearchCancellation) return;
            var knownIds = _marketEntries.Select(x => x.Wallpaper.Id).ToHashSet();
            var entries = page.Wallpapers.Where(x => knownIds.Add(x.Id)).Select(x => new MarketEntry(x)).ToArray();
            foreach (var entry in entries) _marketEntries.Add(entry);
            _marketPage = page.CurrentPage;
            _marketLastPage = page.LastPage;
            _marketTotal = page.Total;
            _marketInitialized = true;
            MarketStatusText.Text = $"{_marketEntries.Count} loaded · {_marketTotal:N0} results on Wallhaven";
            MarketEmptyText.Text = _marketLandscapeOnly
                ? "No matching landscape wallpapers. Try another tag, a lower resolution, or turn off Landscape only."
                : "No matching wallpapers. Try another tag or a lower resolution.";
            _ = LoadMarketThumbnailsAsync(entries, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (request == _marketSearchCancellation && !_closed)
            {
                MarketStatusText.Text = MarketError(ex);
                MarketEmptyText.Text = "Could not load wallpapers. Press Search to retry.";
            }
        }
        finally
        {
            if (request == _marketSearchCancellation && !_closed)
            {
                _marketLoading = false;
                MarketProgress.Visibility = Visibility.Collapsed;
                MarketEmptyText.Visibility = _marketEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                MarketLoadMoreButton.Visibility = _marketPage < _marketLastPage ? Visibility.Visible : Visibility.Collapsed;
                MarketLoadMoreButton.IsEnabled = true;
                UpdateMarketSteps();
                MarketDownloadButton.IsEnabled = _marketSelected != null && _marketMetadata.ContainsKey(_marketSelected.Id)
                    && _marketDownloadedAsset == null && !_marketDownloading;
            }
        }
    }

    private async Task LoadMarketThumbnailsAsync(MarketEntry[] entries, CancellationToken token)
    {
        using var slots = new SemaphoreSlim(3);
        try
        {
            await Task.WhenAll(entries.Select(async entry =>
            {
                await slots.WaitAsync(token);
                try
                {
                    var bytes = await _marketClient.GetThumbnailAsync(entry.Wallpaper.ThumbnailUrl, token);
                    var image = await Task.Run(() => DecodeMarketPreview(bytes), token);
                    if (token.IsCancellationRequested || _closed) return;
                    entry.SetThumbnail(image);
                    if (_marketSelected?.Id == entry.Wallpaper.Id && !_marketPreviewIsFullSize) MarketPreview.Source = image;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception) { if (!token.IsCancellationRequested && !_closed) entry.SetThumbnail(null); }
                finally { slots.Release(); }
            }));
        }
        catch (OperationCanceledException) { }
    }

    private static BitmapImage DecodeMarketPreview(byte[] bytes, int decodePixelWidth = 400)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async void MarketGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _marketDetailCancellation?.Cancel();
        _marketDetailCancellation?.Dispose();
        _marketDetailCancellation = new CancellationTokenSource();
        var token = _marketDetailCancellation.Token;
        var entry = MarketGallery.SelectedItem as MarketEntry;
        ShowMarketSelection(entry);
        if (entry == null) return;
        _ = LoadFocusedMarketPreviewAsync(entry, token);
        try
        {
            if (!_marketMetadata.TryGetValue(entry.Wallpaper.Id, out var details))
                details = await _marketClient.GetWallpaperAsync(entry.Wallpaper.Id, token, _marketAppliedPurity);
            if (token.IsCancellationRequested || _closed) return;
            _marketMetadata[details.Id] = details;
            _marketSelected = details;
            MarketTags.ItemsSource = details.Tags;
            MarketTagStatus.Text = details.Tags.Count == 0 ? "No tags supplied by Wallhaven." : "";
            MarketUploaderText.Text = details.Uploader.Length > 0 ? "Uploaded by " + details.Uploader : "";
            MarketDownloadButton.IsEnabled = !_marketLoading;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_closed)
                MarketTagStatus.Text = MarketError(ex) + " Select this wallpaper again to retry.";
        }
    }

    private void ShowMarketSelection(MarketEntry? entry)
    {
        MarketTagsPopup.IsOpen = false;
        _marketSelected = entry?.Wallpaper;
        _marketDownloadedAsset = null;
        MarketUseButton.Visibility = Visibility.Collapsed;
        MarketDownloadButton.Content = "Download to library";
        MarketDownloadButton.IsEnabled = false;
        MarketTags.ItemsSource = null;
        MarketTagStatus.Text = "Loading website tags…";
        MarketUploaderText.Text = "";
        MarketDetails.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
        // The viewer takes the whole page; the search bar comes back with the grid.
        MarketFilters.Visibility = entry == null ? Visibility.Visible : Visibility.Collapsed;
        UpdateMarketSteps();
        _marketPreviewIsFullSize = false;
        MarketPreviewStatus.Text = entry == null ? "" : "Loading large preview…";
        MarketPreview.Source = entry?.Thumbnail;
        MarketNameText.Text = entry?.Wallpaper.Name ?? "";
        MarketInfoText.Text = entry == null ? "" : $"{entry.Wallpaper.Caption} · {entry.Wallpaper.FileSize / 1048576d:0.0} MB";
        if (entry != null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (MarketGallery.SelectedItem != entry) return;
                MarketGallery.UpdateLayout();
                MarketGallery.ScrollIntoView(entry);
                if (MarketGallery.ItemContainerGenerator.ContainerFromItem(entry) is FrameworkElement card
                    && FindMarketScrollViewer(MarketGallery) is { } viewer)
                    viewer.ScrollToVerticalOffset(viewer.VerticalOffset + card.TranslatePoint(new System.Windows.Point(), viewer).Y);
            }));
    }

    private async Task LoadFocusedMarketPreviewAsync(MarketEntry entry, CancellationToken token)
    {
        try
        {
            var bytes = await _marketClient.GetPreviewAsync(entry.Wallpaper.ImageUrl, token);
            var image = await Task.Run(() => DecodeMarketPreview(bytes, 2560), token);
            if (token.IsCancellationRequested || _closed || _marketSelected?.Id != entry.Wallpaper.Id) return;
            _marketPreviewIsFullSize = true;
            MarketPreview.Source = image;
            MarketPreviewStatus.Text = "";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && !_closed && _marketSelected?.Id == entry.Wallpaper.Id)
                MarketPreviewStatus.Text = "Showing thumbnail · Expand to retry full-size preview";
        }
    }

    /// <summary>Tiles keep a 16:9 shape and stretch so each row fills the page edge to edge.</summary>
    private void MarketGallery_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(200, e.NewSize.Width - 18); // Room for the scroll bar.
        var columns = Math.Max(1, (int)Math.Round(width / 300));
        var tile = Math.Floor(width / columns) - 14; // Ring, padding and margin around each tile.
        MarketGallery.Resources["TileWidth"] = tile;
        MarketGallery.Resources["TileHeight"] = Math.Round(tile * 9 / 16);
    }

    /// <summary>Your own wallpapers fill each row the same way, a little smaller.</summary>
    private void Gallery_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(200, e.NewSize.Width - 18);
        var columns = Math.Max(1, (int)Math.Round(width / 270));
        var tile = Math.Floor(width / columns) - 20; // Card padding, border and margin.
        Gallery.Resources["TileWidth"] = tile;
        Gallery.Resources["TileHeight"] = Math.Round(tile * 9 / 16);
    }

    private bool MarketHasMorePages => _marketPage < _marketLastPage;

    private void UpdateMarketSteps()
    {
        var index = MarketGallery.SelectedIndex;
        MarketPreviousButton.IsEnabled = index > 0 && !_marketDownloading;
        MarketNextButton.IsEnabled = index >= 0 && (index < _marketEntries.Count - 1 || MarketHasMorePages) && !_marketDownloading;
        MarketPositionText.Text = index < 0 ? "" : $"{index + 1} of {Math.Max(_marketTotal, _marketEntries.Count):N0}";
    }

    private void MarketPrevious_Click(object sender, RoutedEventArgs e) => _ = StepMarketAsync(-1);
    private void MarketNext_Click(object sender, RoutedEventArgs e) => _ = StepMarketAsync(1);

    /// <summary>Moves the viewer to the neighbouring wallpaper, fetching the next page when it runs out.</summary>
    private async Task StepMarketAsync(int by)
    {
        if (_marketDownloading || _busy || MarketGallery.SelectedIndex < 0) return;
        var target = MarketGallery.SelectedIndex + by;
        if (target >= _marketEntries.Count && MarketHasMorePages && !_marketLoading
            && _marketSearchCancellation is { IsCancellationRequested: false } request)
            await LoadMarketPageAsync(request);
        if (target < 0 || target >= _marketEntries.Count) return;
        MarketGallery.SelectedIndex = target;
    }

    private void MarketViewer_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (MarketDetails.Visibility != Visibility.Visible || MarketTagsPopup.IsOpen || e.OriginalSource is System.Windows.Controls.TextBox) return;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Left: _ = StepMarketAsync(-1); break;
            case System.Windows.Input.Key.Right: _ = StepMarketAsync(1); break;
            case System.Windows.Input.Key.Escape: MarketBackToGallery_Click(this, e); break;
            default: return;
        }
        e.Handled = true;
    }

    private void MarketBackToGallery_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _marketDownloading) return;
        var selected = MarketGallery.SelectedItem;
        MarketGallery.UnselectAll();
        if (selected != null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => MarketGallery.ScrollIntoView(selected)));
    }

    private void MarketExpandPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_marketSelected is not { } wallpaper) return;
        if (_marketPreviewWindow != null) { _marketPreviewWindow.Activate(); return; }
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var image = new Image { Source = MarketPreview.Source, Stretch = Stretch.Uniform, Margin = new Thickness(20) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "BgBrush");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(20, 14, 20, 0) };
        var close = new Button { Content = "Back to marketplace · Esc", Padding = new Thickness(14, 8, 14, 8) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = wallpaper.Name, FontSize = 18, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(header);
        Grid.SetRow(image, 1);
        root.Children.Add(image);
        var footer = new DockPanel { Margin = new Thickness(20, 0, 20, 18) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var download = new Button { Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(8, 0, 0, 0) };
        download.SetBinding(ContentControl.ContentProperty, new Binding("Content") { Source = MarketDownloadButton });
        download.SetBinding(IsEnabledProperty, new Binding("IsEnabled") { Source = MarketDownloadButton });
        var use = new Button { Content = "Use in profile" };
        use.SetBinding(VisibilityProperty, new Binding("Visibility") { Source = MarketUseButton });
        use.SetBinding(IsEnabledProperty, new Binding("IsEnabled") { Source = MarketUseButton });
        actions.Children.Add(use);
        actions.Children.Add(download);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        var status = new TextBlock { Text = wallpaper.Caption + " · Loading full-size preview…",
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        footer.Children.Add(status);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        var preview = new Window
        {
            Title = wallpaper.Name + " · Preview", Owner = DialogOwner, Content = root,
            Width = 1100, Height = 760, MinWidth = 640, MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowState = WindowState.Maximized
        };
        _marketPreviewWindow = preview;
        UiAppearance.Attach(preview);
        close.Click += (_, _) => preview.Close();
        preview.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) { key.Handled = true; preview.Close(); } };
        preview.Closed += (_, _) => cancellation.Cancel();
        download.Click += (button, args) => { preview.Close(); MarketDownload_Click(button, args); };
        use.Click += (button, args) => { preview.Close(); MarketUse_Click(button, args); };
        var localPath = _marketDownloadedAsset?.FilePath;
        var fullSizePreview = _marketPreviewIsFullSize ? MarketPreview.Source : null;
        preview.Loaded += async (_, _) =>
        {
            try
            {
                ImageSource? fullImage;
                if (fullSizePreview != null) fullImage = fullSizePreview;
                else if (localPath != null && File.Exists(localPath))
                    fullImage = await Task.Run(() => ThumbnailLoader.Load(localPath, 2560), token);
                else
                {
                    var bytes = await _marketClient.GetPreviewAsync(wallpaper.ImageUrl, token);
                    fullImage = await Task.Run(() => DecodeMarketPreview(bytes, 2560), token);
                }
                if (token.IsCancellationRequested) return;
                if (fullImage != null) image.Source = fullImage;
                status.Text = wallpaper.Caption + (fullImage == null ? " · Preview unavailable" : " · Full-size preview");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { if (!token.IsCancellationRequested) status.Text = "Showing thumbnail. " + MarketError(ex); }
        };
        try { preview.ShowDialog(); }
        finally { _marketPreviewWindow = null; }
    }

    private void MarketTags_Click(object sender, RoutedEventArgs e) => MarketTagsPopup.IsOpen = !MarketTagsPopup.IsOpen;

    private void MarketTagsPopup_Opened(object? sender, EventArgs e)
    {
        if (MarketTagsPopup.Child is FrameworkElement content)
            content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void MarketTagsPopup_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        MarketTagsPopup.IsOpen = false;
        MarketTagsButton.Focus();
        e.Handled = true;
    }

    private async void MarketTag_Click(object sender, RoutedEventArgs e)
    {
        if (_marketDownloading || sender is not Button { Tag: int id }) return;
        MarketTagsPopup.IsOpen = false;
        MarketSearchBox.Text = "id:" + id;
        await SearchMarketplaceAsync();
    }

    private async void MarketLoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (_marketSearchCancellation is { IsCancellationRequested: false } request)
            await LoadMarketPageAsync(request);
    }

    private async void MarketGallery_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange > 0 && e.ExtentHeight - e.ViewportHeight - e.VerticalOffset < 180
            && _marketSearchCancellation is { IsCancellationRequested: false } request)
            await LoadMarketPageAsync(request);
    }

    private static ScrollViewer? FindMarketScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var found = FindMarketScrollViewer(VisualTreeHelper.GetChild(parent, i));
            if (found != null) return found;
        }
        return null;
    }

    private async void MarketDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_marketDownloading || _marketLoading || _busy || _marketSelected is not { } wallpaper || !MarketDownloadButton.IsEnabled) return;
        _marketDownloading = true;
        SetBusy(true);
        LocalLibraryTab.IsEnabled = MarketFilters.IsEnabled = MarketGallery.IsEnabled = MarketTags.IsEnabled = false;
        UpdateMarketSteps();
        MarketDownloadButton.IsEnabled = MarketUseButton.IsEnabled = MarketLoadMoreButton.IsEnabled = false;
        MarketCancelButton.Visibility = Visibility.Visible;
        _marketDownloadCancellation?.Dispose();
        _marketDownloadCancellation = new CancellationTokenSource();
        var token = _marketDownloadCancellation.Token;
        MarketProgress.IsIndeterminate = false;
        MarketProgress.Value = 0;
        MarketProgress.Visibility = Visibility.Visible;
        MarketStatusText.Text = "Downloading original wallpaper…";
        try
        {
            var progress = new Progress<int>(value =>
            {
                if (_closed || token.IsCancellationRequested || !_marketDownloading) return;
                MarketProgress.Value = value;
                MarketStatusText.Text = $"Downloading original wallpaper… {value}%";
            });
            var path = await _marketClient.DownloadAsync(wallpaper, _marketDownloadFolder, progress, token);
            token.ThrowIfCancellationRequested();
            var asset = _assets.FirstOrDefault(a => a.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                asset = new WallpaperAsset { FilePath = path, Name = wallpaper.Name, Collection = "Wallhaven",
                    Tags = wallpaper.Tags.Select(t => t.Name).Append(wallpaper.Purity == "sfw" ? "" : wallpaper.Purity)
                        .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
                var updated = _assets.Append(asset).ToList();
                // Finish committing a completed download even if the view is closed during the save.
                await Task.Run(() => _store.Save(updated));
                _assets = updated;
            }
            _marketDownloadedAsset = asset;
            if (_closed) return;
            RefreshCollections();
            MarketStatusText.Text = "Saved to your library with website tags. Ready to use in a profile.";
            MarketDownloadButton.Content = "Downloaded ✓";
            MarketUseButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!_closed) MarketStatusText.Text = "Download canceled.";
        }
        catch (Exception ex) { if (!_closed) MarketStatusText.Text = MarketError(ex) + " Click Download to retry."; }
        finally
        {
            _marketDownloading = false;
            if (!_closed)
            {
                SetBusy(false);
                LocalLibraryTab.IsEnabled = MarketFilters.IsEnabled = MarketGallery.IsEnabled = MarketTags.IsEnabled = true;
                UpdateMarketSteps();
                MarketDownloadButton.IsEnabled = _marketDownloadedAsset == null;
                MarketUseButton.IsEnabled = MarketLoadMoreButton.IsEnabled = true;
                MarketCancelButton.Visibility = MarketProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void MarketCancel_Click(object sender, RoutedEventArgs e) => _marketDownloadCancellation?.Cancel();

    private void MarketUse_Click(object sender, RoutedEventArgs e)
    {
        if (_marketDownloadedAsset is not { } asset || _busy) return;
        if (!File.Exists(asset.FilePath))
        {
            MarketStatusText.Text = "The downloaded file is missing. Download this wallpaper again.";
            _marketDownloadedAsset = null;
            MarketUseButton.Visibility = Visibility.Collapsed;
            MarketDownloadButton.Content = "Download to library";
            MarketDownloadButton.IsEnabled = true;
            return;
        }
        CommitLibrarySelection(new[] { asset });
    }

    private void MarketSource_Click(object sender, RoutedEventArgs e)
    {
        if (_marketSelected == null) return;
        try { Process.Start(new ProcessStartInfo(_marketSelected.PageUrl.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MarketStatusText.Text = "Could not open the source page. " + ex.Message; }
    }

    private static string MarketError(Exception ex)
    {
        Logger.Warn("Wallpaper marketplace: " + ex.Message);
        return ex is OperationCanceledException ? "Wallhaven took too long to respond. Please retry."
            : ex is System.Net.Http.HttpRequestException ? "Could not connect to Wallhaven. " + ex.Message
            : ex is System.Text.Json.JsonException ? "Wallhaven returned an unexpected response. Please retry later."
            : ex.Message;
    }
}
