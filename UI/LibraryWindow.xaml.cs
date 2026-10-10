using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace WallpaperProfiles.UI;

internal partial class LibraryWindow : Window
{
    private const int PageSize = 48;
    private readonly LibraryStore _store;
    private List<WallpaperAsset> _assets;
    private CancellationTokenSource? _thumbnailCancellation;
    private bool _ready;
    private bool _refreshing;
    private bool _busy;
    private bool _closed;
    private int _page;
    private Action<WallpaperAsset>? _inlineSelection;
    private Action<IReadOnlyList<WallpaperAsset>>? _inlineMultipleSelection;
    private readonly HashSet<string> _pickedPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _targetProfileName;
    private Action? _inlineBack;

    private Window DialogOwner => Window.GetWindow(LibraryRoot) ?? this;

    public FrameworkElement CreateInlineContent(Action<WallpaperAsset> selection, Action back,
        Action<IReadOnlyList<WallpaperAsset>>? multipleSelection = null)
    {
        _inlineSelection = selection;
        _inlineMultipleSelection = multipleSelection;
        _inlineBack = back;
        LibraryChrome.Visibility = Visibility.Collapsed;
        LibraryRoot.RowDefinitions[0].Height = new GridLength(0);
        LibraryTitle.Visibility = Visibility.Collapsed;
        LibraryCloseButton.Visibility = Visibility.Collapsed;
        var root = (FrameworkElement)Content;
        Content = null;
        return root;
    }

    public WallpaperAsset? Result { get; private set; }
    public IReadOnlyList<WallpaperAsset> SelectedAssets { get; private set; } = Array.Empty<WallpaperAsset>();

    public void SetTargetProfile(string? name)
    {
        _targetProfileName = name;
        UpdateLibrarySelection();
    }

    private sealed record CollectionChoice(string Label, string? Name);

    public LibraryWindow(LibraryStore store, IEnumerable<string> initialSources,
        WallhavenClient? marketplaceClient = null, string? marketplaceDownloadFolder = null)
    {
        _store = store;
        _assets = store.Load();
        _marketClient = marketplaceClient ?? new WallhavenClient();
        _marketDownloadFolder = marketplaceDownloadFolder ?? Path.Combine(AppPaths.BaseDir, "wallpapers", "wallhaven");
        InitializeComponent();
        InitializeMarketplace();
        LibraryRoot.SizeChanged += (_, _) => UpdateDetailsLayout();
        UiAppearance.Attach(this);
        Closed += (_, _) =>
        {
            _closed = true;
            _thumbnailCancellation?.Cancel();
            _thumbnailCancellation?.Dispose();
        };
        var sources = initialSources.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        async void InitializeLibrary(object sender, RoutedEventArgs e)
        {
            if (_ready) return;
            _ready = true;
            RefreshCollections();
            RefreshGallery();
            if (sources.Length > 0) await ImportAsync(sources);
        }
        Loaded += InitializeLibrary;
        LibraryRoot.Loaded += InitializeLibrary;
    }

    private sealed class GalleryEntry : INotifyPropertyChanged
    {
        public WallpaperAsset Asset { get; }
        public bool Exists { get; }
        private bool _previewLoaded;
        public string Placeholder => !Exists ? "Source missing" : !_previewLoaded ? "Loading preview…" : "Preview unavailable";
        public string Caption => (Asset.IsFavorite ? "★ · " : "")
            + (WallpaperEngine.IsVideoFile(Asset.FilePath) ? "Video" : "Image")
            + (Asset.Collection.Length > 0 ? " · " + Asset.Collection : "");
        private ImageSource? _thumbnail;
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value; _previewLoaded = true;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Placeholder)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private bool _isPicked;
        private readonly Action<GalleryEntry> _pickedChanged;
        public bool IsPicked
        {
            get => _isPicked;
            set
            {
                if (_isPicked == value) return;
                _isPicked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPicked)));
                _pickedChanged(this);
            }
        }
        public GalleryEntry(WallpaperAsset asset, bool picked, Action<GalleryEntry> pickedChanged)
        {
            Asset = asset;
            Exists = File.Exists(asset.FilePath);
            _isPicked = picked;
            _pickedChanged = pickedChanged;
        }
    }

    private void RefreshCollections()
    {
        _refreshing = true;
        var selected = (CollectionFilter.SelectedItem as CollectionChoice)?.Name;
        var collections = _assets.Select(a => a.Collection).Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var filters = new[] { new CollectionChoice("All collections", null) }
            .Concat(collections.Select(c => new CollectionChoice(c, c))).ToArray();
        CollectionFilter.ItemsSource = filters;
        CollectionFilter.SelectedItem = filters.FirstOrDefault(c => string.Equals(c.Name, selected, StringComparison.OrdinalIgnoreCase)) ?? filters[0];
        AssetCollectionBox.ToolTip = collections.Length > 0 ? "Existing collections: " + string.Join(", ", collections) : "Type a name to create a collection.";
        _refreshing = false;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _refreshing) return;
        _page = 0;
        RefreshGallery();
    }

    private void RefreshGallery(string? selectPath = null)
    {
        if (!_ready || _closed) return;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        var token = _thumbnailCancellation.Token;
        var search = SearchBox.Text.Trim();
        var collection = (CollectionFilter.SelectedItem as CollectionChoice)?.Name;
        var matches = _assets.Where(a =>
                (FavoritesFilter.IsChecked != true || a.IsFavorite)
                && (collection == null || a.Collection.Equals(collection, StringComparison.OrdinalIgnoreCase))
                && (search.Length == 0 || a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || a.Collection.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || a.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var pageCount = Math.Max(1, (matches.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pageCount - 1);
        if (selectPath != null)
        {
            var index = matches.FindIndex(a => a.FilePath.Equals(selectPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _page = index / PageSize;
        }
        _pickedPaths.IntersectWith(_assets.Select(a => a.FilePath));
        var entries = matches.Skip(_page * PageSize).Take(PageSize)
            .Select(a => new GalleryEntry(a, _pickedPaths.Contains(a.FilePath), PickedChanged)).ToArray();
        Gallery.ItemsSource = entries;
        Gallery.SelectedItem = entries.FirstOrDefault(a => a.Asset.FilePath.Equals(selectPath, StringComparison.OrdinalIgnoreCase));
        ShowSelection();
        EmptyText.Text = _assets.Count == 0 ? "+ Import your first wallpaper" : "No matching wallpapers";
        EmptyText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"{matches.Count} shown · {_assets.Count} in library";
        PageText.Text = $"Page {_page + 1} of {pageCount}";
        PreviousButton.IsEnabled = _page > 0;
        NextButton.IsEnabled = _page + 1 < pageCount;
        _ = LoadThumbnailsAsync(entries, token);
    }

    private async Task LoadThumbnailsAsync(GalleryEntry[] entries, CancellationToken token)
    {
        // Limit both visible thumbnails and concurrent image/video decoding.
        using var slots = new SemaphoreSlim(3);
        try
        {
            await Task.WhenAll(entries.Where(a => a.Exists).Select(async entry =>
            {
                await slots.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    var image = await Task.Run(() => ThumbnailLoader.Load(entry.Asset.FilePath, 400), token);
                    if (!token.IsCancellationRequested && !_closed) entry.Thumbnail = image;
                }
                finally { slots.Release(); }
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logger.Error("Loading library thumbnails failed.", ex); }
    }

    private void Gallery_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ShowSelection();
    private void Gallery_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UseButton.IsEnabled && e.OriginalSource is DependencyObject source)
        {
            var item = System.Windows.Controls.ItemsControl.ContainerFromElement(Gallery, source);
            if (item != null) Use_Click(sender, e);
        }
    }

    private void ShowSelection()
    {
        if (Gallery.SelectedItem is not GalleryEntry entry)
        {
            DetailsFrame.Visibility = Visibility.Collapsed; DetailsColumn.Width = new GridLength(0);
            LocalGalleryFrame.Visibility = Visibility.Visible;
            UseButton.Visibility = Visibility.Collapsed;
            DetailsPanel.Visibility = Visibility.Collapsed;
            SelectionHint.Visibility = Visibility.Visible;
            UseButton.IsEnabled = false;
            UpdateLibrarySelection();
            return;
        }
        DetailsPanel.Visibility = Visibility.Visible;
        DetailsFrame.Visibility = Visibility.Visible; UpdateDetailsLayout();
        UseButton.Visibility = Visibility.Visible;
        SelectionHint.Visibility = Visibility.Collapsed;
        AssetNameBox.Text = entry.Asset.Name;
        TagsBox.Text = string.Join(", ", entry.Asset.Tags);
        AssetCollectionBox.Text = entry.Asset.Collection;
        FavoriteBox.IsChecked = entry.Asset.IsFavorite;
        SourceText.Text = entry.Asset.FilePath;
        MissingText.Visibility = entry.Exists ? Visibility.Collapsed : Visibility.Visible;
        UseButton.IsEnabled = entry.Exists && !_busy;
        UpdateLibrarySelection();
    }

    private void UpdateDetailsLayout()
    {
        var selected = Gallery.SelectedItem != null;
        var compact = LibraryRoot.ActualWidth > 0 && LibraryRoot.ActualWidth < 780;
        LocalGalleryFrame.Visibility = selected && compact ? Visibility.Collapsed : Visibility.Visible;
        DetailsColumn.Width = new GridLength(selected && !compact ? 280 : 0);
        System.Windows.Controls.Grid.SetColumn(DetailsFrame, compact ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(DetailsFrame, compact ? 2 : 1);
        BackToCollectionButton.Visibility = selected && compact ? Visibility.Visible : Visibility.Collapsed;
    }
    private void BackToCollection_Click(object sender, RoutedEventArgs e) => Gallery.SelectedIndex = -1;

    private async Task ImportAsync(IEnumerable<string> sources)
    {
        if (_busy) return;
        SetBusy(true);
        StatusText.Text = "Importing wallpapers…";
        try
        {
            var existing = _assets.ToArray();
            var added = await Task.Run(() => LibraryStore.Import(existing, sources));
            if (_closed) return;
            if (added.Count > 0)
            {
                var updated = _assets.Concat(added).ToList();
                await Task.Run(() => _store.Save(updated));
                if (_closed) return;
                _assets = updated;
                RefreshCollections();
                RefreshGallery();
            }
            StatusText.Text = added.Count > 0 ? $"Imported {added.Count} wallpaper(s) · {_assets.Count} in library"
                : "No new wallpapers found. Existing entries and unsupported files were skipped.";
        }
        catch (Exception ex) { ReportError("Import failed", ex); }
        finally { if (!_closed) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ImportFilesButton.IsEnabled = !busy;
        ImportFolderButton.IsEnabled = !busy;
        MarketplaceTab.IsEnabled = !busy;
        Gallery.IsEnabled = !busy;
        DetailsPanel.IsEnabled = !busy;
        UseButton.IsEnabled = !busy && Gallery.SelectedItem is GalleryEntry entry && entry.Exists;
        UpdateLibrarySelection();
    }

    private async void ImportFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import wallpapers", Multiselect = true,
            Filter = "Wallpaper media|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.jfif;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mpg;*.mpeg;*.m2v;*.mkv;*.webm;*.3gp|All files|*.*"
        };
        if (dialog.ShowDialog(DialogOwner) == true) await ImportAsync(dialog.FileNames);
    }

    private async void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var folder = ModernFolderPicker.Pick(DialogOwner, "Import a wallpaper folder", null);
        if (folder != null) await ImportAsync(new[] { folder });
    }

    private async void SaveDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Gallery.SelectedItem is not GalleryEntry entry) return;
        var name = AssetNameBox.Text.Trim();
        if (name.Length == 0)
        {
            StatusText.Text = "Enter a name before saving.";
            AssetNameBox.Focus();
            return;
        }
        var replacement = new WallpaperAsset
        {
            FilePath = entry.Asset.FilePath, Name = name, IsFavorite = FavoriteBox.IsChecked == true,
            Collection = AssetCollectionBox.Text.Trim(),
            Tags = TagsBox.Text.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
        var updated = _assets.Select(a => ReferenceEquals(a, entry.Asset) ? replacement : a).ToList();
        await SaveChangesAsync(updated, replacement.FilePath, "Details saved.");
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Gallery.SelectedItem is not GalleryEntry entry) return;
        await SaveChangesAsync(_assets.Where(a => !ReferenceEquals(a, entry.Asset)).ToList(), null,
            "Removed from library. Your original file and profiles are kept.");
    }

    private async Task SaveChangesAsync(List<WallpaperAsset> updated, string? selectPath, string success)
    {
        SetBusy(true);
        try
        {
            await Task.Run(() => _store.Save(updated));
            if (_closed) return;
            _assets = updated;
            RefreshCollections();
            RefreshGallery(selectPath);
            StatusText.Text = success;
        }
        catch (Exception ex) { ReportError("Save failed", ex); }
        finally { if (!_closed) SetBusy(false); }
    }

    private void ReportError(string title, Exception ex)
    {
        Logger.Error(title + " in wallpaper library.", ex);
        if (_closed) return;
        StatusText.Text = "Changes could not be saved. Your previous library is kept.";
        System.Windows.MessageBox.Show(DialogOwner, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var assets = PickedAssets();
        if (assets.Count == 0) return;
        if (assets.Any(asset => !File.Exists(asset.FilePath)))
        {
            StatusText.Text = "A selected source file is missing. Restore it or remove it from your selection.";
            return;
        }
        CommitLibrarySelection(assets);
    }

    private IReadOnlyList<WallpaperAsset> PickedAssets() => _pickedPaths.Count > 0
        ? _assets.Where(a => _pickedPaths.Contains(a.FilePath)).ToArray()
        : Gallery.SelectedItem is GalleryEntry entry ? new[] { entry.Asset } : Array.Empty<WallpaperAsset>();

    private void PickedChanged(GalleryEntry entry)
    {
        if (entry.IsPicked) _pickedPaths.Add(entry.Asset.FilePath);
        else _pickedPaths.Remove(entry.Asset.FilePath);
        UpdateLibrarySelection();
    }

    private void UpdateLibrarySelection()
    {
        var selected = PickedAssets();
        var addingToProfile = _inlineMultipleSelection != null || _targetProfileName != null;
        UseButton.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UseButton.IsEnabled = !_busy && selected.Count > 0 && selected.All(a => File.Exists(a.FilePath));
        UseButton.Content = !addingToProfile ? "Use in profile"
            : selected.Count > 1 ? $"Add {selected.Count} wallpapers" : "Add to profile";
        LibrarySelectionText.Text = !addingToProfile ? (_pickedPaths.Count > 0 ? $"{_pickedPaths.Count} selected" : "")
            : (_targetProfileName == null ? "Create a new profile" : "Adding to " + _targetProfileName)
                + (_pickedPaths.Count > 0 ? $" · {_pickedPaths.Count} selected" : " · Select using the checkboxes");
        ClearPickedButton.Visibility = _pickedPaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearPickedButton.IsEnabled = !_busy;
    }

    private void ClearPicked_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _pickedPaths.Clear();
        foreach (var entry in Gallery.Items.OfType<GalleryEntry>()) entry.IsPicked = false;
        UpdateLibrarySelection();
    }

    private void CommitLibrarySelection(IReadOnlyList<WallpaperAsset> assets)
    {
        SelectedAssets = assets;
        Result = assets[0];
        _pickedPaths.Clear();
        foreach (var entry in Gallery.Items.OfType<GalleryEntry>()) entry.IsPicked = false;
        if (_inlineMultipleSelection != null) _inlineMultipleSelection(assets);
        else if (_inlineSelection != null) _inlineSelection(assets[0]);
        else DialogResult = true;
    }

    private void Previous_Click(object sender, RoutedEventArgs e) { _page--; RefreshGallery(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _page++; RefreshGallery(); }
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_inlineBack != null) _inlineBack();
        else Close();
    }
    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (_inlineBack == null && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
