using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using Brush = System.Windows.Media.Brush;
using DragDropEffects = System.Windows.DragDropEffects;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
    // ============ Path validation & preview ============

    private async void ValidatePath()
    {
        if (Dispatcher.HasShutdownStarted) return;
        try { await Dispatcher.InvokeAsync(ValidatePathAsync).Task.Unwrap(); }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted) { }
    }

    private async Task ValidatePathAsync()
    {
        var path = FolderBox.Text.Trim();
        var version = ++_pathVersion;
        ++_previewRequest;
        SetPreviewMedia(Array.Empty<string>());
        if (path.Length > 0 || _additionalWallpapers.Count > 0)
        { PreviewEmptyTitle.Text = "Loading preview…"; PreviewEmptyHint.Text = "You can keep editing while it loads."; }
        else
        { PreviewEmptyTitle.Text = "Choose a wallpaper"; PreviewEmptyHint.Text = "Drop an image or video here to get started."; }

        if (_additionalWallpapers.Count > 0)
        {
            var extraPaths = _additionalWallpapers.Select(a => a.FilePath).ToList();
            var media = await Task.Run(() => WallpaperEngine.GetProfileMedia(new WallpaperProfile
                { FolderPath = path, AdditionalWallpaperPaths = extraPaths, WallpaperOrder = _wallpaperOrder.ToList() }));
            if (version != _pathVersion) return;
            var missing = extraPaths.Count(source => !File.Exists(source));
            SetPathStatus(media.Count > 0 ? "GoodBrush" : "DangerBrush",
                $"{media.Count} {(media.Count == 1 ? "wallpaper" : "wallpapers")}" + (missing > 0 ? $" · {missing} missing" : ""));
            var first = media.FirstOrDefault();
            var thumbnail = await Task.Run(() => ThumbnailLoader.Load(first, 900));
            if (version != _pathVersion) return;
            PreviewMon.PreviewFilePath = first ?? "";
            PreviewMon.ScreenSource = thumbnail;
            if (thumbnail == null && first != null) { PreviewEmptyTitle.Text = "Preview unavailable"; PreviewEmptyHint.Text = "The source is available. Choose another image to preview."; }
            PreviewMon.IsVideo = first != null && WallpaperEngine.IsLiveFile(first);
            SetPreviewMedia(media);
            return;
        }

        if (path.Length == 0)
        {
            SetPathStatus("TextTertiaryBrush", "No location set");
            PreviewMon.ScreenSource = null;
            return;
        }

        var info = await Task.Run(() => PathInspector.Inspect(path));
        if (version != _pathVersion)
        {
            return;
        }

        if (!info.Exists)
        {
            SetPathStatus("DangerBrush", "Path not found");
            PreviewMon.ScreenSource = null;
            PreviewMon.IsVideo = false;
            return;
        }

        if (info.IsFile)
        {
            if (info.VideoCount > 0)
            {
                SetPathStatus("GoodBrush", "Single video · live wallpaper");
            }
            else if (info.ImageCount > 0)
            {
                SetPathStatus("GoodBrush", "Single image");
            }
            else
            {
                SetPathStatus("DangerBrush", "Not an image or video");
            }
        }
        else if (info.ImageCount == 1 && info.VideoCount == 0)
        {
            SetPathStatus("GoodBrush", "Folder · 1 image");
        }
        else if (info.ImageCount == 0 && info.VideoCount > 0)
        {
            SetPathStatus("DangerBrush",
                $"Folder · {info.VideoCount} video(s) — pick a video file directly for a live wallpaper");
        }
        else
        {
            var text = $"Folder · {info.ImageCount} images";
            if (info.VideoCount > 0)
            {
                text += $" · {info.VideoCount} video(s)";
            }
            SetPathStatus("GoodBrush", text);
        }

        var available = await Task.Run(() => WallpaperEngine.GetProfileMedia(new WallpaperProfile { FolderPath = path, WallpaperOrder = _wallpaperOrder.ToList() }));
        if (version != _pathVersion) return;
        var previewFile = available.FirstOrDefault();
        var image = previewFile is null
            ? null
            : await Task.Run(() => ThumbnailLoader.Load(previewFile, 900));
        if (version != _pathVersion)
        {
            return;
        }
        var previous = PreviewMon.ScreenSource;
        PreviewMon.PreviewFilePath = previewFile ?? "";
        PreviewMon.ScreenSource = image;
        if (image == null && PreviewMon.PreviewFilePath.Length > 0) { PreviewEmptyTitle.Text = "Preview unavailable"; PreviewEmptyHint.Text = "The source is available. Choose another image to preview."; }
        PreviewMon.IsVideo = info.IsFile && info.VideoCount > 0;
        if (image != null && !ReferenceEquals(previous, image))
        {
            PreviewMon.Pulse();
        }
        if (version == _pathVersion) SetPreviewMedia(available);
    }

    private void SetPreviewMedia(IReadOnlyList<string> media)
    {
        _previewMedia = media;
        ProfileWallpaperList.SetWallpapers(media);
        _previewIndex = 0;
        PreviousPreviewButton.IsEnabled = NextPreviewButton.IsEnabled = media.Count > 1;
        PreviewPositionText.Text = media.Count > 0 ? $"1 / {media.Count}" : "Desktop preview";
    }

    private void WallpaperOrder_Changed(object sender, RoutedEventArgs e)
    {
        var media = ProfileWallpaperList.Items.Cast<string>().ToList();
        _wallpaperOrder = media.Concat(_wallpaperOrder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ++_pathVersion;
        ++_previewRequest;
        _previewMedia = media;
        _previewIndex = Math.Max(0, media.FindIndex(path => path.Equals(PreviewMon.PreviewFilePath, StringComparison.OrdinalIgnoreCase)));
        PreviewPositionText.Text = media.Count > 0 ? $"{_previewIndex + 1} / {media.Count}" : "Desktop preview";
        UpdateDraftState();
    }

    private async void BrowsePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewMedia.Count < 2 || sender is not System.Windows.Controls.Button { Tag: string step }) return;
        _previewIndex = (_previewIndex + int.Parse(step, CultureInfo.InvariantCulture) + _previewMedia.Count) % _previewMedia.Count;
        var request = ++_previewRequest;
        var version = _pathVersion;
        var file = _previewMedia[_previewIndex];
        PreviewPositionText.Text = $"{_previewIndex + 1} / {_previewMedia.Count}";
        var image = await Task.Run(() => ThumbnailLoader.Load(file, 1200));
        if (request != _previewRequest || version != _pathVersion) return;
        PreviewMon.PreviewFilePath = file;
        PreviewMon.ScreenSource = image;
        if (image == null && PreviewMon.PreviewFilePath.Length > 0) { PreviewEmptyTitle.Text = "Preview unavailable"; PreviewEmptyHint.Text = "The source is available. Choose another image to preview."; }
        PreviewMon.IsVideo = WallpaperEngine.IsLiveFile(file);
        if (SystemParameters.ClientAreaAnimation) PreviewMon.Pulse();
    }

    private void ExpandPreview_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewMon.ScreenSource == null) return;
        var content = new Grid { Background = System.Windows.Media.Brushes.Black };
        content.Children.Add(new System.Windows.Controls.Image { Source = PreviewMon.ScreenSource, Stretch = Stretch.Uniform });
        var preview = new Window { Owner = this, Title = "Wallpaper preview", WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized, Content = content, Background = System.Windows.Media.Brushes.Black };
        var close = new System.Windows.Controls.Button { Content = "Close preview  ×", HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top, Margin = new Thickness(24), Padding = new Thickness(16, 10, 16, 10),
            Style = (Style)FindResource("PrimaryButton") };
        close.Click += (_, _) => preview.Close();
        content.Children.Add(close);
        preview.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) preview.Close(); };
        preview.ShowDialog();
    }

    private void SetPathStatus(string brushKey, string text)
    {
        PathStatusDot.Fill = (Brush)FindResource(brushKey);
        PathStatusText.Text = text;
        PathStatusPanel.Visibility = brushKey == "DangerBrush" || text.Contains("missing", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
        if (brushKey == "DangerBrush")
        { PreviewEmptyTitle.Text = "Source unavailable"; PreviewEmptyHint.Text = text + ". Choose another source or restore the file."; }
    }

    // ============ Pickers & drag-drop ============

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var current = FolderBox.Text.Trim();
        var start = current.Length > 0 ? current : _selected?.FolderPath;
        var picked = ModernFolderPicker.Pick(this, "Choose a wallpaper folder", start);
        if (picked != null)
        {
            FolderBox.Text = picked;
        }
    }

    private void PickImage_Click(object sender, RoutedEventArgs e) => PickMediaFile(video: false);
    private void PickVideo_Click(object sender, RoutedEventArgs e) => PickMediaFile(video: true);

    private void PickMediaFile(bool video)
    {
        var path = DropHelper.PickMediaFile(FolderBox.Text.Trim(), video);
        if (path != null) FolderBox.Text = path;
    }

    private void DropZone_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (DropHelper.CanAccept(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void DropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        var path = DropHelper.ExtractPath(e.Data);
        if (path != null)
        {
            FolderBox.Text = path;
        }
    }
}
