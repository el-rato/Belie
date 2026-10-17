using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace WallpaperProfiles.UI;

internal sealed partial class DesktopCanvasView
{
    private sealed record WallpaperChoice(string Path, string Label);
    private readonly ComboBox _wallpaperPicker = new() { DisplayMemberPath = nameof(WallpaperChoice.Label), MinWidth = 140, MaxWidth = 260, Margin = new Thickness(0, 0, 8, 6) };
    private readonly CheckBox _followDesktop = new() { Content = "Follow desktop", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 6) };
    private readonly TextBlock _layoutStatus = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private Button _customizeLayout = null!, _resetLayout = null!;
    private readonly Grid _galleryFrame = new();
    private readonly StackPanel _skinControls = new();
    private readonly ComboBox _clockSkin = new() { ItemsSource = Enum.GetValues<ClockSkin>() };
    private readonly ComboBox _spectrumSkin = new() { ItemsSource = Enum.GetValues<SpectrumSkin>() };
    private readonly Slider _density = new() { Minimum = 12, Maximum = 96, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider _spectrumThickness = new() { Minimum = 1, Maximum = 10, TickFrequency = .5, IsSnapToTickEnabled = true };
    private readonly Slider _sensitivity = new() { Minimum = .25, Maximum = 3, TickFrequency = .05, IsSnapToTickEnabled = true };
    private readonly Slider _smoothing = new() { Minimum = 0, Maximum = .95, TickFrequency = .05, IsSnapToTickEnabled = true };
    private readonly TextBox _positionX = new(), _positionY = new();
    private string _canvasPath = "";
    private int _wallpaperRequest;
    private bool _settingWallpaper;
    private string _backdropPath = "";
    private System.Windows.Media.Brush? _backdropBrush;
    private double _loadedX, _loadedY;
    private (Guid Id, Point Start, double X, double Y)? _canvasDrag;
    private string CanvasPath => _canvasPath;
    private Guid? CanvasProfile => _scopeFilter.SelectedItem is ProfileChoice { All: false } choice ? choice.Id : _activeProfile();

    internal void OpenWallpaper(Guid profileId, string path)
    {
        if (!TryLeaveDraft()) return;
        CloseEditor(); _canvasPath = path; _followDesktop.IsChecked = false;
        _scopeFilter.SelectedItem = _scopeFilter.Items.Cast<ProfileChoice>().FirstOrDefault(p => !p.All && p.Id == profileId);
        LoadWallpaperChoices();
    }

    private FrameworkElement BuildCanvasToolbar()
    {
        var panel = new StackPanel();
        var choices = new WrapPanel();
        choices.Children.Add(_wallpaperPicker); choices.Children.Add(_followDesktop);
        _customizeLayout = ActionButton("Customize wallpaper", "CustomizeWallpaperLayoutButton", CustomizeLayout);
        _resetLayout = ActionButton("Use profile default", "ResetWallpaperLayoutButton", ResetLayout);
        foreach (var button in new[] { _customizeLayout, _resetLayout,
            ActionButton("Save layout", "SaveWidgetLayoutButton", SaveLayoutPreset),
            ActionButton("Saved layouts", "BrowseWidgetLayoutsButton", () => OpenCatalogue(false, true)) })
        { button.Padding = new Thickness(8, 6, 8, 6); button.FontSize = 12; button.Margin = new Thickness(0, 0, 8, 6); choices.Children.Add(button); }
        panel.Children.Add(choices); panel.Children.Add(_layoutStatus);
        RegisterName("WallpaperCanvasPicker", _wallpaperPicker); RegisterName("FollowDesktopWallpaper", _followDesktop);
        System.Windows.Automation.AutomationProperties.SetName(_wallpaperPicker, "Wallpaper canvas");
        _layoutStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return panel;
    }

    private void InitializeStudio()
    {
        _wallpaperPicker.SelectionChanged += (_, _) =>
        {
            if (_settingWallpaper) return;
            var choice = _wallpaperPicker.SelectedItem as WallpaperChoice;
            if (!TryLeaveDraft())
            {
                _settingWallpaper = true; _wallpaperPicker.SelectedItem = _wallpaperPicker.Items.Cast<WallpaperChoice>().FirstOrDefault(p => DesktopCanvasDocument.SameWallpaper(p.Path, _canvasPath)); _settingWallpaper = false;
                return;
            }
            CloseEditor(); _canvasPath = choice?.Path ?? ""; _followDesktop.IsChecked = false; _overviewKey = ""; RefreshList();
        };
        _followDesktop.Checked += (_, _) => { if (_selected == null) FollowDesktop(_store.Load()); };
        foreach (var combo in new[] { _clockSkin, _spectrumSkin }) combo.SelectionChanged += (_, _) => RefreshPreview();
        foreach (var slider in new[] { _density, _spectrumThickness, _sensitivity, _smoothing }) slider.ValueChanged += (_, _) => RefreshPreview();
        foreach (var box in new[] { _positionX, _positionY }) box.TextChanged += (_, _) => RefreshPreview();
        _desktopOverview.MouseMove += (_, e) =>
        {
            if (_canvasDrag is not { } drag || _selected?.Id != drag.Id || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(_desktopOverview);
            _loading = true;
            _positionX.Text = Math.Round(drag.X + point.X - drag.Start.X).ToString(CultureInfo.InvariantCulture);
            _positionY.Text = Math.Round(drag.Y + point.Y - drag.Start.Y).ToString(CultureInfo.InvariantCulture);
            _loading = false; RefreshPreview();
        };
        _desktopOverview.MouseLeftButtonUp += (_, _) => { _canvasDrag = null; _desktopOverview.ReleaseMouseCapture(); };
        _desktopOverview.LostMouseCapture += (_, _) => _canvasDrag = null;
    }

    private void BuildSkinControls()
    {
        var commands = new WrapPanel();
        commands.Children.Add(ActionButton("Browse skins", "BrowseWidgetSkinsButton", () => OpenCatalogue(true)));
        commands.Children.Add(ActionButton("Save skin", "SaveWidgetSkinButton", SaveSkin));
        commands.Children.Add(ActionButton("Export skin", "ExportWidgetSkinButton", ExportSkin));
        _skinControls.Children.Add(commands);
        Field(_skinControls, "Clock design", _clockSkin);
        Field(_skinControls, "Visualizer shape", _spectrumSkin);
        Field(_skinControls, "Density", _density); Field(_skinControls, "Line thickness", _spectrumThickness);
        Field(_skinControls, "Audio sensitivity", _sensitivity); Field(_skinControls, "Motion smoothing", _smoothing);
        _skinControls.Children.Add(ActionButton("Use wallpaper colors", "MatchWallpaperColorsButton", MatchWallpaperColors));
        _stylePage.Children.Add(_skinControls);
        foreach (var (name, control) in new (string, FrameworkElement)[] { ("ClockSkinPicker", _clockSkin), ("VisualizerSkinPicker", _spectrumSkin),
            ("VisualizerDensity", _density), ("VisualizerThickness", _spectrumThickness), ("VisualizerSensitivity", _sensitivity), ("VisualizerSmoothing", _smoothing),
            ("WidgetPositionX", _positionX), ("WidgetPositionY", _positionY) })
        { RegisterName(name, control); System.Windows.Automation.AutomationProperties.SetName(control, name); }
    }

    private void LoadSkin(DesktopWidget widget)
    {
        _clockSkin.SelectedItem = widget.ClockSkin; _spectrumSkin.SelectedItem = widget.SpectrumSkin;
        _density.Value = widget.SpectrumDensity; _spectrumThickness.Value = widget.SpectrumThickness;
        _sensitivity.Value = widget.SpectrumSensitivity; _smoothing.Value = widget.SpectrumSmoothing;
        _positionX.Text = widget.X.ToString(CultureInfo.InvariantCulture); _positionY.Text = widget.Y.ToString(CultureInfo.InvariantCulture);
        _loadedX = widget.X; _loadedY = widget.Y;
        _skinControls.Visibility = widget.Kind is DesktopWidgetKind.Clock or DesktopWidgetKind.Visualizer or DesktopWidgetKind.NowPlaying ? Visibility.Visible : Visibility.Collapsed;
        _clockSkin.Visibility = widget.Kind == DesktopWidgetKind.Clock ? Visibility.Visible : Visibility.Collapsed;
        if (_clockSkin.Parent is StackPanel parent)
        {
            var labelIndex = parent.Children.IndexOf(_clockSkin) - 1;
            if (labelIndex >= 0) parent.Children[labelIndex].Visibility = _clockSkin.Visibility;
        }
        _profile.IsEnabled = string.IsNullOrEmpty(widget.WallpaperPath);
    }

    private void ReadSkin(DesktopWidget widget)
    {
        widget.ClockSkin = _clockSkin.SelectedItem is ClockSkin clock ? clock : ClockSkin.Script;
        widget.SpectrumSkin = _spectrumSkin.SelectedItem is SpectrumSkin spectrum ? spectrum : SpectrumSkin.Mirrored;
        widget.SpectrumDensity = (int)_density.Value; widget.SpectrumThickness = _spectrumThickness.Value;
        widget.SpectrumSensitivity = _sensitivity.Value; widget.SpectrumSmoothing = _smoothing.Value;
    }

    private void ReadPosition(DesktopWidget widget, bool changedOnly = false)
    {
        if (double.TryParse(_positionX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) && (!changedOnly || x != _loadedX)) widget.X = Math.Clamp(x, -100000, 100000);
        if (double.TryParse(_positionY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && double.IsFinite(y) && (!changedOnly || y != _loadedY)) widget.Y = Math.Clamp(y, -100000, 100000);
    }

    private async void LoadWallpaperChoices()
    {
        var request = ++_wallpaperRequest;
        var profileId = CanvasProfile;
        var profile = _profiles().FirstOrDefault(p => p.Id == profileId);
        var oldPath = _canvasPath;
        var choices = new List<WallpaperChoice> { new("", "Profile default · all wallpapers") };
        try
        {
            if (profile != null)
            {
                var media = await Task.Run(() => WallpaperEngine.GetProfileMedia(profile)).ConfigureAwait(false);
                if (request != _wallpaperRequest) return;
                choices.AddRange(media.Select(path => new WallpaperChoice(path, Path.GetFileName(path))));
            }
            if (Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (request != _wallpaperRequest) return;
                _settingWallpaper = true;
                _wallpaperPicker.ItemsSource = choices;
                _wallpaperPicker.SelectedItem = choices.FirstOrDefault(choice => DesktopCanvasDocument.SameWallpaper(choice.Path, oldPath)) ?? choices[0];
                _canvasPath = (_wallpaperPicker.SelectedItem as WallpaperChoice)?.Path ?? "";
                _wallpaperPicker.IsEnabled = profile != null; _settingWallpaper = false;
                _overviewKey = ""; RefreshLayoutActions(_store.Load());
                if (_selected == null) RefreshList();
            });
        }
        catch (Exception ex)
        {
            if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(new Action(() => { _settingWallpaper = false; _message.Text = ex.Message; }));
        }
    }

    private void FollowDesktop(DesktopCanvasDocument document)
    {
        if (_followDesktop.IsChecked != true || _selected != null) return;
        if (CanvasProfile != document.ActiveProfileId)
        {
            var choice = _scopeFilter.Items.Cast<ProfileChoice>().FirstOrDefault(p => !p.All && p.Id == document.ActiveProfileId);
            if (choice != null) _scopeFilter.SelectedItem = choice;
            return;
        }
        if (DesktopCanvasDocument.SameWallpaper(_canvasPath, document.ActiveWallpaperPath)) return;
        var wallpaper = _wallpaperPicker.Items.Cast<WallpaperChoice>().FirstOrDefault(p => DesktopCanvasDocument.SameWallpaper(p.Path, document.ActiveWallpaperPath));
        if (wallpaper == null) return;
        _settingWallpaper = true; _canvasPath = wallpaper.Path; _wallpaperPicker.SelectedItem = wallpaper; _settingWallpaper = false;
        _overviewKey = ""; RefreshList();
    }

    private void RefreshLayoutActions(DesktopCanvasDocument document)
    {
        var custom = document.HasLayout(CanvasProfile, CanvasPath);
        _customizeLayout.Visibility = !string.IsNullOrEmpty(CanvasPath) && !custom ? Visibility.Visible : Visibility.Collapsed;
        _resetLayout.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _layoutStatus.Text = string.IsNullOrEmpty(CanvasPath) ? "Default composition · used by wallpapers without a custom layout."
            : custom ? "Custom composition · restored automatically whenever this wallpaper plays."
            : "Using the profile default. Choose Customize wallpaper to arrange an independent look.";
    }

    private string EditableCanvasPath() => _store.Load().HasLayout(CanvasProfile, CanvasPath) ? CanvasPath : "";

    private void CustomizeLayout()
    {
        if (!TryLeaveDraft() || CanvasProfile is not { } profile || string.IsNullOrEmpty(CanvasPath)) return;
        CloseEditor(); _store.Update(document => document.CustomizeWallpaper(profile, CanvasPath));
        _overviewKey = ""; RefreshList(); _startHost();
    }

    private void ResetLayout()
    {
        if (!TryLeaveDraft() || CanvasProfile is not { } profile || string.IsNullOrEmpty(CanvasPath)) return;
        var owner = Window.GetWindow(this);
        if (System.Windows.MessageBox.Show(owner!, "Replace this wallpaper’s custom composition with the profile default?", "Use profile default", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        CloseEditor(); _store.Update(document => document.ResetWallpaper(profile, CanvasPath));
        _overviewKey = ""; RefreshList(); _startHost();
    }

    private System.Windows.Media.Brush? CanvasWallpaperBrush()
    {
        var path = PreviewWallpaperPath();
        if (_backdropPath == path) return _backdropBrush;
        _backdropPath = path; _backdropBrush = null;
        _ = LoadCanvasBackdrop(path);
        return null;
    }

    private async Task LoadCanvasBackdrop(string path)
    {
        var image = await Task.Run(() => ThumbnailLoader.Load(path, 1920)).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (_backdropPath != path) return;
            _backdropBrush = image == null ? null : new ImageBrush(image) { Stretch = _profiles().FirstOrDefault(p => p.Id == CanvasProfile)?.FitMode switch
                { Models.FitMode.Fit => Stretch.Uniform, Models.FitMode.Stretch => Stretch.Fill, _ => Stretch.UniformToFill } };
            _overviewKey = "";
            if (_selected == null) RefreshOverview(_store.Load()); else RefreshPreview();
        });
    }

    private string PreviewWallpaperPath() => !string.IsNullOrEmpty(CanvasPath) ? CanvasPath
        : CanvasProfile == _activeProfile() ? _store.Load().ActiveWallpaperPath
        : _wallpaperPicker.Items.Cast<WallpaperChoice>().FirstOrDefault(p => p.Path.Length > 0)?.Path ?? "";

    private void StartCanvasDrag(DesktopWidget widget, MouseButtonEventArgs e)
    {
        if (_selected?.Id != widget.Id)
        {
            _list.SelectedItem = _list.Items.Cast<DesktopWidget>().FirstOrDefault(w => w.Id == widget.Id);
            if (_selected?.Id != widget.Id) return;
        }
        if (widget.Locked) return;
        var point = e.GetPosition(_desktopOverview);
        _canvasDrag = (widget.Id, point, widget.X, widget.Y); _desktopOverview.CaptureMouse(); e.Handled = true;
    }

    private DesktopWidget DraftWidget()
    {
        var widget = WidgetStyles.Copy(_selected ?? new DesktopWidget());
        widget.Id = _selected?.Id ?? Guid.NewGuid(); widget.Title = _title.Text; widget.Content = _content.Text;
        widget.Width = _width.Value; widget.Height = _height.Value; ReadAppearance(widget); ReadPosition(widget);
        return widget;
    }

    private void OpenCatalogue(bool forSelected, bool layouts = false)
    {
        var catalogue = new WidgetStyleCatalogWindow(_store, PreviewWallpaperPath(), forSelected ? _selected?.Kind : null) { Owner = Window.GetWindow(this) };
        if (layouts) catalogue.ShowLayouts();
        if (catalogue.ShowDialog() != true) return;
        if (catalogue.SelectedLayout is { } layout)
        {
            if (!TryLeaveDraft()) return;
            var owner = Window.GetWindow(this);
            if (System.Windows.MessageBox.Show(owner!, "Replace this canvas’s widgets with a copy of “" + layout.Name + "”?", "Apply saved layout", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            CloseEditor();
            _store.Update(document =>
            {
                if (!string.IsNullOrEmpty(CanvasPath) && CanvasProfile is { } profile) document.CustomizeWallpaper(profile, CanvasPath);
                var path = document.HasLayout(CanvasProfile, CanvasPath) ? CanvasPath : "";
                document.Widgets.RemoveAll(w => w.ProfileId == CanvasProfile && DesktopCanvasDocument.SameWallpaper(w.WallpaperPath, path));
                foreach (var widget in layout.Widgets)
                {
                    var copy = WidgetStyles.Copy(widget); copy.ProfileId = CanvasProfile; copy.WallpaperPath = path; document.Widgets.Add(copy);
                }
                document.Enabled = true;
            });
            _overviewKey = ""; RefreshList(); _startHost(); return;
        }
        if (catalogue.SelectedStyle is not { } style) return;
        if (forSelected && _selected != null)
        {
            var draft = DraftWidget(); var savedSnapshot = _savedDraft; var isNew = _isNew;
            if (style.Kind == DesktopWidgetKind.Visualizer && draft.Kind is DesktopWidgetKind.Clock or DesktopWidgetKind.NowPlaying)
            {
                draft.SpectrumSkin = style.SpectrumSkin; draft.SpectrumDensity = style.SpectrumDensity; draft.SpectrumThickness = style.SpectrumThickness;
                draft.SpectrumSensitivity = style.SpectrumSensitivity; draft.SpectrumSmoothing = style.SpectrumSmoothing;
                if (draft.Kind == DesktopWidgetKind.Clock) draft.ClockShowVisualizer = true; else draft.ShowAudioVisualizer = true;
            }
            else if (draft.Kind == style.Kind) WidgetStyles.Apply(draft, style);
            else { _message.Text = "Choose a skin for this widget type, or add it from the studio catalogue."; return; }
            _selected = null; Select(draft, isNew); _savedDraft = savedSnapshot; SetEditorTab(1); RefreshPreview();
        }
        else
        {
            if (!TryLeaveDraft()) return;
            var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            style.ProfileId = string.IsNullOrEmpty(CanvasPath) ? (_scopeFilter.SelectedItem as ProfileChoice)?.Id : CanvasProfile;
            style.WallpaperPath = EditableCanvasPath(); style.X = area.Left + (area.Width - style.Width) / 2; style.Y = area.Top + area.Height * .2;
            _selected = null; Select(style, true); SetEditorTab(1);
        }
    }

    private string? AskName(string title)
    {
        var owner = Window.GetWindow(this);
        var dialog = new Window { Title = title, Owner = owner, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(24) };
        var input = new TextBox { MaxLength = 100, Margin = new Thickness(0, 0, 0, 18) };
        System.Windows.Automation.AutomationProperties.SetName(input, "Name"); panel.Children.Add(input);
        var save = new Button { Content = "Save", IsDefault = true, IsEnabled = false, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        input.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        save.Click += (_, _) => dialog.DialogResult = true; panel.Children.Add(save);
        UiAppearance.SetDialogContent(dialog, panel, title); dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void SaveLayoutPreset()
    {
        if (!TryLeaveDraft()) return;
        var name = AskName("Save layout"); if (name == null) return;
        _store.Update(document =>
        {
            var widgets = document.Composition(CanvasProfile, CanvasPath, !CanvasProfile.HasValue).Select(WidgetStyles.Copy).ToList();
            document.LayoutPresets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            document.LayoutPresets.Add(new() { Name = name, Widgets = widgets });
        });
        _message.Text = "Layout saved to the catalogue.";
    }

    private void SaveSkin()
    {
        if (_selected == null) return;
        var name = AskName("Save skin"); if (name == null) return;
        var widget = DraftWidget(); if (widget.Kind == DesktopWidgetKind.NowPlaying) widget.Kind = DesktopWidgetKind.Visualizer;
        _store.Update(document => { document.StylePresets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); document.StylePresets.Add(new() { Name = name, Style = widget }); });
        _message.Text = "Skin saved in My styles. Your widget draft is still available.";
    }

    private void ExportSkin()
    {
        if (_selected == null) return;
        var picker = new Microsoft.Win32.SaveFileDialog { Filter = "Belie skin (*.json)|*.json", FileName = "My Belie skin.json" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        var widget = DraftWidget(); if (widget.Kind == DesktopWidgetKind.NowPlaying) widget.Kind = DesktopWidgetKind.Visualizer;
        File.WriteAllText(picker.FileName, JsonSerializer.Serialize(new WidgetStylePreset { Name = widget.DisplayTitle, Style = widget }, JsonOptions.Shared));
        _message.Text = "Skin exported.";
    }

    private void MatchWallpaperColors()
    {
        if (ThumbnailLoader.Load(PreviewWallpaperPath(), 64) is not BitmapSource source) { _message.Text = "Choose an image wallpaper first."; return; }
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(bytes, image.PixelWidth * 4, 0);
        double luminance = 0, best = -1; var accent = Color.FromRgb(200, 218, 210);
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var r = bytes[i + 2]; var g = bytes[i + 1]; var b = bytes[i];
            luminance += (.2126 * r + .7152 * g + .0722 * b) / 255;
            var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
            var score = max - min - Math.Abs(max - 175) * .35;
            if (score > best) { best = score; accent = Color.FromRgb(r, g, b); }
        }
        var darkInk = luminance / (bytes.Length / 4) > .55;
        var neutral = darkInk ? 24 : 248;
        var tint = Color.FromRgb((byte)(accent.R * .25 + neutral * .75), (byte)(accent.G * .25 + neutral * .75), (byte)(accent.B * .25 + neutral * .75));
        _loading = true; _textColor.Text = $"#{tint.R:X2}{tint.G:X2}{tint.B:X2}";
        _musicAccent.Text = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}"; _borderColor.Text = _musicAccent.Text;
        _loading = false; RefreshPreview();
    }
}
