using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Models;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;
using Control = System.Windows.Controls.Control;
using ComboBox = System.Windows.Controls.ComboBox;

namespace WallpaperProfiles.UI;

internal sealed partial class DesktopCanvasView
{
    private sealed record ColorOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;
    private void ColorField(string label, TextBox input, StackPanel panel)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(input);
        var choose = new Button { Content = "Choose", Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(choose, 1); row.Children.Add(choose);
        choose.Click += (_, _) =>
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
            if (SceneController.TryParseAccent(input.Text, out var color)) dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
            var window = Window.GetWindow(this);
            if (window == null) return;
            if (dialog.ShowDialog(new ColorOwner(new System.Windows.Interop.WindowInteropHelper(window).Handle)) == System.Windows.Forms.DialogResult.OK)
                input.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        };
        Field(panel, label, row);
    }
    private void ReadAppearance(DesktopWidget widget)
    {
        widget.FontFamily = string.IsNullOrWhiteSpace(_font.Text) ? "Segoe UI" : _font.Text.Trim();
        widget.FontSize = _fontSize.Value; widget.Bold = _bold.IsChecked == true;
        widget.Italic = _italic.IsChecked == true;
        widget.Alignment = _alignment.SelectedItem is WidgetTextAlignment alignment ? alignment : WidgetTextAlignment.Left;
        widget.ImageFit = _imageFit.SelectedItem is WidgetImageFit fit ? fit : WidgetImageFit.Fit;
        widget.TextColor = _textColor.Text.Trim().ToUpperInvariant(); widget.BackgroundColor = _backgroundColor.Text.Trim().ToUpperInvariant();
        widget.Opacity = _opacity.Value / 100; widget.CornerRadius = _radius.Value;
        widget.ShowHeader = _showHeader.IsChecked == true; widget.ShowBorder = _showBorder.IsChecked == true;
        widget.ShowBackground = _showBackground.IsChecked == true;
        widget.GlassEffect = _glassEffect.IsChecked == true;
        widget.MusicLayout = _musicLayout.SelectedItem is NowPlayingLayout layout ? layout : NowPlayingLayout.Horizontal;
        widget.ShowAlbumArt = _albumArt.IsChecked == true;
        widget.ShowPlaybackControls = _playbackControls.IsChecked == true;
        widget.ShowPlaybackProgress = _playbackProgress.IsChecked == true;
        widget.ShowAudioVisualizer = _audioVisualizer.IsChecked == true;
        widget.MusicAccentColor = _musicAccent.Text.Trim().ToUpperInvariant();
        widget.ClockUse24Hour = _clock24Hour.IsChecked == true;
        widget.ClockShowGreeting = _clockGreeting.IsChecked == true;
        widget.ClockShowVisualizer = _clockVisualizer.IsChecked == true;
        ReadSkin(widget);
        widget.BorderColor = _borderColor.Text.Trim().ToUpperInvariant(); widget.BorderWidth = _borderWidth.Value;
    }
    private void QueuePreview()
    {
        if (_loading || _previewPending) return;
        _previewPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            _previewPending = false;
            RefreshPreview();
        }));
    }
    private void RefreshPreview()
    {
        if (_loading || _selected == null) return;
        _save.IsEnabled = IsDraftDirty;
        if (IsDraftDirty) _message.Text = "Unsaved changes";
        else if (_message.Text == "Unsaved changes") _message.Text = "";
        var draft = _selected.Copy(); draft.Id = _selected.Id;
        draft.Title = _title.Text; draft.Content = _content.Text;
        draft.Enabled = _enabled.IsChecked == true;
        draft.LinkCustomDescription = _linkDescription.Text; draft.LeetCodeUsername = _leetCodeUser.Text.Trim();
        draft.Locked = _locked.IsChecked == true;
        draft.Width = _width.Value; draft.Height = _height.Value;
        ReadAppearance(draft);
        if (_date.SelectedDate is { } date && TimeOnly.TryParseExact(_time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            var local = DateTime.SpecifyKind(date.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
            if (!TimeZoneInfo.Local.IsInvalidTime(local)) draft.Target = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }
        _previewWindow ??= new DesktopWidgetWindow(draft, _store);
        _previewWindow.UpdatePreview(draft, DesktopWidgetWindow.PreviewScale(draft));
        if (_preview.Child == null)
        {
            var content = (FrameworkElement)_previewWindow.Content;
            _previewWindow.Content = null;
            _preview.Child = content;
        }
        var previewContent = (FrameworkElement)_preview.Child;
        previewContent.Width = draft.Width; previewContent.Height = draft.Height;
        ReadPosition(draft);
        RefreshOverview(_store.Load(), draft);
    }
    private void ApplyStyle(string name)
    {
        if (_selected == null) return;
        _loading = true;
        if (name == "Glass") _glassEffect.IsChecked = true;
        string Available(params string[] fonts) => fonts.FirstOrDefault(font => InstalledFonts.Contains(font, StringComparer.OrdinalIgnoreCase)) ?? "Georgia";
        var gothic = Available("Old English Text MT", "UnifrakturCook", "UnifrakturMaguntia", "Gabriola");
        var style = name switch
        {
            "Glass" => ("Segoe UI", "#182333", "#FFFFFF", "#E1EFFF", 28d),
            "Paper" => ("Georgia", "#EEE8D8", "#282A25", "#738579", 3d),
            "Gothic" => (gothic, "#141018", "#E5D8F4", "#715577", 4d),
            "Cathedral" => (gothic, "#1B1A22", "#E9DEBF", "#A39169", 0d),
            "Crimson" => (gothic, "#270D15", "#F1D5CB", "#9D5263", 8d),
            "Parchment" => (Available("Gabriola", "Palatino Linotype"), "#E4D3AA", "#463421", "#A18A5F", 3d),
            "Royal" => (Available("Garamond", "Palatino Linotype"), "#20162D", "#EADBAF", "#A58B51", 16d),
            "Neon" => (Available("Bahnschrift", "Segoe UI"), "#10221F", "#80FFCE", "#4DDDB3", 12d),
            "Terminal" => (Available("Cascadia Mono", "Consolas"), "#0D1711", "#B7D9AC", "#477A52", 4d),
            _ => ("Segoe UI", "#1D201E", "#F1F2EE", "#738579", 16d)
        };
        _font.Text = style.Item1; _backgroundColor.Text = style.Item2;
        _textColor.Text = style.Item3; _borderColor.Text = style.Item4; _radius.Value = style.Item5;
        _borderWidth.Value = 1; _italic.IsChecked = false;
        if (name is "Gothic" or "Cathedral" or "Crimson") { _bold.IsChecked = false; _fontSize.Value = Math.Max(26, _fontSize.Value); }
        _opacity.Value = 100;
        _showHeader.IsChecked = name != "Minimal"; _showBorder.IsChecked = name != "Minimal";
        _showBackground.IsChecked = name != "Minimal" || _glassEffect.IsChecked == true;
        if (name == "Rainmeter")
        {
            _showHeader.IsChecked = _showBorder.IsChecked = _showBackground.IsChecked = _glassEffect.IsChecked = false;
            if (_selected?.Kind == DesktopWidgetKind.Clock)
            {
                _font.Text = ClockWidgetView.ScriptFont; _fontSize.Value = 76;
                _width.Value = 520; _height.Value = 310; _alignment.SelectedItem = WidgetTextAlignment.Center;
            }
            if (_selected?.Kind == DesktopWidgetKind.NowPlaying)
            {
                _musicLayout.SelectedItem = NowPlayingLayout.Minimal;
                _albumArt.IsChecked = _playbackControls.IsChecked = false; _playbackProgress.IsChecked = true;
                _fontSize.Value = 24; _width.Value = 360; _height.Value = _audioVisualizer.IsChecked == true ? 220 : 140;
                _musicAccent.Text = _textColor.Text;
            }
        }
        _loading = false; RefreshPreview();
    }
}
