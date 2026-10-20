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

internal sealed partial class DesktopCanvasView : UserControl
{
    private readonly DesktopCanvasStore _store;
    private readonly Action _startHost;
    private readonly Func<IReadOnlyList<WallpaperProfile>> _profiles;
    private readonly Func<Guid?> _activeProfile;
    private readonly Func<string> _activeWallpaper;
    private sealed record ProfileChoice(Guid? Id, string Label, bool All = false);
    private readonly ComboBox _profile = new() { DisplayMemberPath = nameof(ProfileChoice.Label), MaxDropDownHeight = 280 };
    private readonly ComboBox _scopeFilter = new() { DisplayMemberPath = nameof(ProfileChoice.Label), Width = 190, Margin = new Thickness(0, 0, 16, 0) };
    private readonly Border _overviewFrame = new();
    private readonly System.Windows.Controls.Canvas _desktopOverview = new();
    private readonly TextBlock _overviewCaption = new();
    private readonly ColumnDefinition _galleryColumn = new() { Width = new GridLength(208) };
    private readonly ListBox _list = new() { MinHeight = 100, DisplayMemberPath = nameof(DesktopWidget.Title) };
    private readonly TextBox _title = new();
    private readonly TextBox _content = new() { TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 110,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DatePicker _date = new();
    private readonly TextBox _time = new() { Text = "18:00" };
    private readonly Slider _width = new() { Minimum = 120, Maximum = 1600, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly Slider _height = new() { Minimum = 80, Maximum = 1200, TickFrequency = 10, IsSnapToTickEnabled = true };
    private static readonly string[] InstalledFonts = Fonts.SystemFontFamilies.Select(font => font.Source)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    private readonly ComboBox _font = new() { IsEditable = true, ItemsSource = InstalledFonts, MaxDropDownHeight = 320 };
    private readonly Slider _fontSize = new() { Minimum = 10, Maximum = 96, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly ComboBox _alignment = new() { ItemsSource = Enum.GetValues<WidgetTextAlignment>() };
    private readonly ComboBox _imageFit = new() { ItemsSource = Enum.GetValues<WidgetImageFit>() };
    private readonly TextBox _textColor = new() { MaxLength = 7 };
    private readonly TextBox _backgroundColor = new() { MaxLength = 7 };
    private readonly TextBox _borderColor = new() { MaxLength = 7 };
    private readonly Slider _borderWidth = new() { Minimum = 0, Maximum = 6, TickFrequency = .5, IsSnapToTickEnabled = true };
    private readonly Slider _opacity = new() { Minimum = 20, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider _radius = new() { Minimum = 0, Maximum = 60, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly CheckBox _bold = new() { Content = "Bold text" };
    private readonly CheckBox _italic = new() { Content = "Italic text" };
    private readonly CheckBox _showHeader = new() { Content = "Show title bar" };
    private readonly CheckBox _showBorder = new() { Content = "Show border" };
    private readonly CheckBox _showBackground = new() { Content = "Show background" };
    private readonly CheckBox _glassEffect = new() { Content = "Apply glass to this style", Margin = new Thickness(0, 4, 0, 0) };
    private readonly Viewbox _preview = new() { Height = 140, Stretch = Stretch.Uniform, IsHitTestVisible = false, Margin = new Thickness(0, 8, 0, 12) };
    private DesktopWidgetWindow? _previewWindow;
    private readonly Button _duplicate = new() { Content = "Duplicate", Margin = new Thickness(8, 0, 0, 0) };
    private Button _save = null!;
    private bool _loading;
    private bool _previewPending;
    private readonly CheckBox _enabled = new() { Content = "Show this widget", Margin = new Thickness(0, 12, 0, 0) };
    private readonly CheckBox _locked = new() { Content = "Lock position and size", Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _fields = new();
    private readonly StackPanel _targetFields = new();
    private readonly TextBlock _contentLabel = new();
    private readonly TextBlock _kindLabel = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _browse = new() { Content = "Choose image", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _delete = new() { Content = "Remove widget", Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _empty = new() { Text = "Choose a widget or add one above.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly DispatcherTimer _timer;
    private DesktopWidget? _selected;
    private bool _isNew;
    private double _loadedWidth, _loadedHeight;
    private readonly StackPanel _linkFields = new();
    private readonly TextBox _linkDescription = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _leetCodeUser = new();
    private readonly StackPanel _musicFields = new();
    private readonly StackPanel _clockFields = new();
    private readonly CheckBox _clock24Hour = new() { Content = "Use 24-hour time" };
    private readonly CheckBox _clockGreeting = new() { Content = "Show greeting" };
    private readonly CheckBox _clockVisualizer = new() { Content = "Show desktop audio visualizer" };
    private readonly ComboBox _musicLayout = new() { ItemsSource = Enum.GetValues<NowPlayingLayout>() };
    private readonly CheckBox _albumArt = new() { Content = "Show album art" };
    private readonly CheckBox _playbackControls = new() { Content = "Show playback controls" };
    private readonly CheckBox _playbackProgress = new() { Content = "Show progress and time" };
    private readonly CheckBox _audioVisualizer = new() { Content = "Show audio spectrum", ToolTip = "Live bars react to desktop audio from your default speakers or headphones." };
    private readonly TextBox _musicAccent = new() { MaxLength = 7 };
    private readonly StackPanel _contentPage = new(), _stylePage = new(), _layoutPage = new();
    private readonly ScrollViewer _editorScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Border _editorFrame = new();
    private readonly ColumnDefinition _editorColumn = new() { Width = new GridLength(340) };
    private readonly System.Windows.Controls.Primitives.Popup _addPopup = new() { StaysOpen = false, AllowsTransparency = true, PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None };
    private readonly List<Button> _tabButtons = new();
    private Button _showAll = null!, _hideAll = null!;
    private string _overviewKey = "";

    private string _savedDraft = "";
    private ProfileChoice? _previousScope;
    internal Func<DraftChoice>? ChooseDraftAction { get; set; }
    internal Func<bool>? ConfirmDeleteAction { get; set; }
    private string DraftSnapshot() => DraftGuard.Snapshot(new { Title = _title.Text, Content = _content.Text,
        Profile = (_profile.SelectedItem as ProfileChoice)?.Id, Date = _date.SelectedDate, Time = _time.Text,
        Width = _width.Value, Height = _height.Value, Font = _font.Text, Size = _fontSize.Value,
        Alignment = _alignment.SelectedItem, Fit = _imageFit.SelectedItem, TextColor = _textColor.Text,
        Background = _backgroundColor.Text, Border = _borderColor.Text, BorderWidth = _borderWidth.Value,
        Opacity = _opacity.Value, Radius = _radius.Value, Bold = _bold.IsChecked, Italic = _italic.IsChecked,
        Header = _showHeader.IsChecked, HasBorder = _showBorder.IsChecked, HasBackground = _showBackground.IsChecked,
        Glass = _glassEffect.IsChecked, Enabled = _enabled.IsChecked, Locked = _locked.IsChecked,
        Description = _linkDescription.Text, User = _leetCodeUser.Text,
        MusicLayout = _musicLayout.SelectedItem, Art = _albumArt.IsChecked, Controls = _playbackControls.IsChecked,
        Progress = _playbackProgress.IsChecked, AudioVisualizer = _audioVisualizer.IsChecked, MusicAccent = _musicAccent.Text,
        Clock24Hour = _clock24Hour.IsChecked, ClockGreeting = _clockGreeting.IsChecked, ClockVisualizer = _clockVisualizer.IsChecked,
        ClockSkin = _clockSkin.SelectedItem, Spectrum = _spectrumSkin.SelectedItem, Density = _density.Value,
        Thickness = _spectrumThickness.Value, Sensitivity = _sensitivity.Value, Smoothing = _smoothing.Value,
        X = _positionX.Text, Y = _positionY.Text });
    internal bool IsDraftDirty => !_loading && _selected != null && (_isNew || _savedDraft != DraftSnapshot());
    internal bool TryLeaveDraft()
    {
        if (!IsDraftDirty) return true;
        return DraftGuard.Confirm(Window.GetWindow(this), "this widget", () =>
        {
            try { Save(); return !IsDraftDirty; }
            catch (Exception ex) { _message.Text = ex.Message; return false; }
        }, () =>
        {
            if (_isNew) { _selected = null; _fields.Visibility = Visibility.Collapsed; SetEditorVisible(false); }
            else if (_selected != null)
            {
                var saved = _store.Load().Widgets.FirstOrDefault(w => w.Id == _selected.Id);
                _savedDraft = DraftSnapshot();
                if (saved != null) Select(saved, false);
            }
        }, ChooseDraftAction);
    }

    public DesktopCanvasView(DesktopCanvasStore? store = null, Action? startHost = null,
        Func<IReadOnlyList<WallpaperProfile>>? profiles = null, Func<Guid?>? activeProfile = null, Func<string>? activeWallpaper = null)
    {
        _store = store ?? new DesktopCanvasStore(DesktopCanvasProcess.DefaultFile);
        _startHost = startHost ?? (() => DesktopCanvasProcess.Start(_store.FilePath));
        _profiles = profiles ?? (() => Array.Empty<WallpaperProfile>());
        _activeProfile = activeProfile ?? (() => _store.Load().ActiveProfileId);
        _activeWallpaper = activeWallpaper ?? (() => _store.Load().ActiveWallpaperPath);
        NameScope.SetNameScope(this, new NameScope());
        Content = BuildWorkspace();
        foreach (var preview in new FrameworkElement[] { _preview, _list, _desktopOverview })
            TextOptions.SetTextRenderingMode(preview, TextRenderingMode.Grayscale);
        SizeChanged += (_, _) => _preview.Height = ActualHeight < 640 ? 48 : 96;
        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true; };
        PreviewDrop += (_, e) => { Run(() => CreateFromDrop(e.Data)); e.Handled = true; };
        RegisterName("LinkDescription", _linkDescription); RegisterName("LeetCodeUsername", _leetCodeUser);
        RegisterName("WidgetList", _list); RegisterName("WidgetTitle", _title); RegisterName("WidgetContent", _content);
        RegisterName("WidgetProfile", _profile); RegisterName("WidgetScopeFilter", _scopeFilter); RegisterName("DesktopOverview", _overviewFrame);
        System.Windows.Automation.AutomationProperties.SetName(_profile, "Widget profile");
        System.Windows.Automation.AutomationProperties.SetName(_scopeFilter, "Filter widgets by profile");
        _scopeFilter.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (!TryLeaveDraft())
            {
                _loading = true; _scopeFilter.SelectedItem = _previousScope; _loading = false; return;
            }
            _previousScope = _scopeFilter.SelectedItem as ProfileChoice;
            LoadWallpaperChoices();
            Run(RefreshList);
        };
        RegisterName("WidgetDate", _date); RegisterName("WidgetTime", _time); RegisterName("WidgetEnabled", _enabled);
        RegisterName("WidgetWidth", _width); RegisterName("WidgetHeight", _height);
        RegisterName("WidgetLocked", _locked); RegisterName("CanvasMessage", _message);
        foreach (var (name, control) in new (string, FrameworkElement)[] { ("WidgetFont", _font), ("WidgetFontSize", _fontSize),
            ("WidgetAlignment", _alignment), ("WidgetImageFit", _imageFit), ("WidgetTextColor", _textColor),
            ("WidgetBackgroundColor", _backgroundColor), ("WidgetOpacity", _opacity), ("WidgetRadius", _radius),
            ("WidgetBold", _bold), ("WidgetItalic", _italic), ("WidgetBorderColor", _borderColor), ("WidgetBorderWidth", _borderWidth),
            ("WidgetHeader", _showHeader), ("WidgetBorder", _showBorder),
            ("WidgetBackground", _showBackground), ("WidgetGlassEffect", _glassEffect), ("WidgetPreview", _preview) })
        { RegisterName(name, control); System.Windows.Automation.AutomationProperties.SetName(control, name[6..]); }
        foreach (var (name, element) in new (string, DependencyObject)[] { ("Widget title", _title), ("Widget content", _content),
            ("Countdown date", _date), ("Countdown time", _time), ("Widget width", _width), ("Widget height", _height) })
            System.Windows.Automation.AutomationProperties.SetName(element, name);
        _list.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (_list.SelectedItem is DesktopWidget widget)
            {
                if (_selected?.Id != widget.Id && !TryLeaveDraft())
                {
                    _loading = true; _list.SelectedItem = _list.Items.Cast<DesktopWidget>().FirstOrDefault(w => w.Id == _selected?.Id); _loading = false;
                    return;
                }
                Select(widget, false);
            }
            else if (_selected != null)
            {
                if (!TryLeaveDraft())
                {
                    _loading = true; _list.SelectedItem = _list.Items.Cast<DesktopWidget>().FirstOrDefault(w => w.Id == _selected?.Id); _loading = false;
                }
                else CloseEditor();
            }
        };
        _browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose a canvas image", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) _content.Text = dialog.FileName;
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshStatus();
        IsVisibleChanged += (_, _) => _timer.IsEnabled = IsVisible;
        Unloaded += (_, _) => { _timer.Stop(); _addPopup.IsOpen = false; ClearCanvasPreviews(); };
        Loaded += (_, _) => { RefreshProfiles(); Run(RefreshList); RefreshStatus(); _timer.Start(); };
        _fields.Visibility = Visibility.Collapsed;
        _save.IsEnabled = false; _duplicate.IsEnabled = false; _delete.IsEnabled = false;
        foreach (var box in new[] { _title, _content, _textColor, _backgroundColor, _borderColor, _time, _linkDescription, _leetCodeUser, _musicAccent }) box.TextChanged += (_, _) => RefreshPreview();
        _musicLayout.SelectionChanged += (_, _) => RefreshPreview();
        foreach (var option in new[] { _albumArt, _playbackControls, _playbackProgress, _audioVisualizer, _clock24Hour, _clockGreeting, _clockVisualizer })
        { option.Checked += (_, _) => RefreshPreview(); option.Unchecked += (_, _) => RefreshPreview(); }
        _audioVisualizer.Checked += (_, _) =>
        {
            if (!_loading && _selected?.Kind == DesktopWidgetKind.NowPlaying)
                _height.Value = Math.Max(_height.Value, _musicLayout.SelectedItem is NowPlayingLayout.Stacked ? 380 : 220);
        };
        foreach (var (name, element) in new (string, FrameworkElement)[] { ("WidgetMusicLayout", _musicLayout), ("WidgetAlbumArt", _albumArt),
            ("WidgetPlaybackControls", _playbackControls), ("WidgetPlaybackProgress", _playbackProgress), ("WidgetMusicAccent", _musicAccent), ("WidgetAudioVisualizer", _audioVisualizer) })
        { RegisterName(name, element); System.Windows.Automation.AutomationProperties.SetName(element, name[6..]); }
        _font.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => QueuePreview()), handledEventsToo: true);
        foreach (var slider in new[] { _width, _height, _fontSize, _opacity, _radius, _borderWidth }) slider.ValueChanged += (_, _) => RefreshPreview();
        _font.SelectionChanged += (_, _) => QueuePreview();
        foreach (var combo in new[] { _alignment, _imageFit }) combo.SelectionChanged += (_, _) => RefreshPreview();
        foreach (var box in new[] { _bold, _italic, _showHeader, _showBorder, _showBackground, _locked })
        { box.Checked += (_, _) => RefreshPreview(); box.Unchecked += (_, _) => RefreshPreview(); }
        _glassEffect.Checked += (_, _) =>
        {
            if (_loading) return;
            _showBackground.IsChecked = true;
            RefreshPreview();
        };
        _glassEffect.Unchecked += (_, _) => RefreshPreview();
        _date.SelectedDateChanged += (_, _) => RefreshPreview();
        _profile.SelectionChanged += (_, _) => RefreshPreview();
        _enabled.Checked += (_, _) => RefreshPreview(); _enabled.Unchecked += (_, _) => RefreshPreview();
        InitializeStudio(); RefreshProfiles(); Run(RefreshList);
    }

    internal void CreateFromDrop(System.Windows.IDataObject data)
    {
        if (!TryLeaveDraft()) return;
        var widgets = DesktopCanvasFeatures.ReadDrop(data);
        foreach (var widget in widgets)
        {
            widget.ProfileId = string.IsNullOrEmpty(CanvasPath) ? (_scopeFilter.SelectedItem as ProfileChoice)?.Id : CanvasProfile;
            widget.WallpaperPath = EditableCanvasPath();
        }
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        DesktopCanvasFeatures.SaveDrop(_store, widgets, area.Right - 340, area.Top + 70);
        _selected = widgets[0]; RefreshList(); _startHost(); _message.Text = $"Created {widgets.Count} widget(s).";
    }
    private void Run(Action action)
    {
        try { _message.Text = ""; action(); }
        catch (Exception ex) { _message.Text = ex.Message; }
    }
    private void RefreshStatus()
    {
        try
        {
            var document = _store.Load();
            var count = document.Widgets.Count;
            var visible = document.Enabled ? document.VisibleWidgets().Count() : 0;
            _status.Text = count == 0 ? "No widgets yet" : $"{visible} active \u00B7 {count} total";
            _showAll.Visibility = document.Enabled ? Visibility.Collapsed : Visibility.Visible;
            _hideAll.Visibility = document.Enabled ? Visibility.Visible : Visibility.Collapsed;
            FollowDesktop(document);
            if (_overviewFrame.Visibility == Visibility.Visible && _selected == null) RefreshOverview(document);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void RefreshList()
    {
        var selected = _selected?.Id;
        var document = _store.Load();
        var filter = _scopeFilter.SelectedItem as ProfileChoice;
        var widgets = (string.IsNullOrEmpty(CanvasPath)
            ? document.Widgets.Where(w => (filter == null || filter.All || w.ProfileId == filter.Id) && string.IsNullOrEmpty(w.WallpaperPath))
            : document.Composition(CanvasProfile, CanvasPath)).ToList();
        var loading = _loading; _loading = true;
        _list.ItemsSource = widgets;
        _list.SelectedItem = widgets.FirstOrDefault(w => w.Id == selected);
        _loading = loading;
        if (!loading && !IsDraftDirty && _list.SelectedItem is DesktopWidget current) Select(current, false);
        _empty.Text = document.Widgets.Count == 0 ? "Drop an image, link, or note here.\nOr add your first widget." : "No widgets in this group.";
        _empty.Visibility = widgets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshStatus();
        RefreshLayoutActions(document);
    }
    private string ProfileLabel(Guid? id) => id.HasValue ? _profiles().FirstOrDefault(profile => profile.Id == id)?.Name ?? "Missing profile" : "General · all profiles";

    internal void RefreshProfiles()
    {
        var choices = new List<ProfileChoice> { new(null, "General · all profiles") };
        choices.AddRange(_profiles().Select(profile => new ProfileChoice(profile.Id, profile.Name)));
        if (_profile.SelectedItem is ProfileChoice saved && saved.Id.HasValue && choices.All(choice => choice.Id != saved.Id))
            choices.Add(new(saved.Id, "Missing profile"));
        if (!_profile.Items.Cast<ProfileChoice>().SequenceEqual(choices))
        {
            var loading = _loading; _loading = true;
            var selected = (_profile.SelectedItem as ProfileChoice)?.Id;
            var filter = _scopeFilter.SelectedItem as ProfileChoice;
            _profile.ItemsSource = choices; _profile.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected) ?? choices[0];
            var filters = new[] { new ProfileChoice(null, "All widgets", true) }.Concat(choices).ToList();
            _scopeFilter.ItemsSource = filters; _scopeFilter.SelectedItem = filters.FirstOrDefault(choice => choice.Id == filter?.Id && choice.All == filter?.All) ?? filters[0];
            _previousScope = _scopeFilter.SelectedItem as ProfileChoice;
            _loading = loading;
            if (!_loading && _selected == null) Run(RefreshList);
            LoadWallpaperChoices();
        }
        RefreshStatus();
    }

    private readonly Dictionary<Guid, (DesktopWidgetWindow Renderer, Border Item, FrameworkElement Content)> _canvasPreviews = new();
    private bool _overviewHadDraft;

    private void ClearCanvasPreviews()
    {
        _desktopOverview.Children.Clear();
        foreach (var preview in _canvasPreviews.Values) preview.Renderer.Close();
        _canvasPreviews.Clear();
        _overviewKey = "";
        _overviewHadDraft = false;
    }

    private void RefreshOverview(DesktopCanvasDocument document, DesktopWidget? draft = null)
    {
        var active = CanvasProfile;
        var widgets = document.Composition(active, CanvasPath).Where(widget => widget.Enabled).ToList();
        if (draft != null) { widgets.RemoveAll(widget => widget.Id == draft.Id); if (draft.Enabled) widgets.Add(draft); }
        _overviewCaption.Text = (active.HasValue ? ProfileLabel(active) : "Global")
            + (string.IsNullOrEmpty(CanvasPath) ? " · Default" : " · " + Path.GetFileName(CanvasPath))
            + (document.Enabled ? "" : " · Hidden");
        var area = System.Windows.Forms.SystemInformation.VirtualScreen;
        var key = $"{File.GetLastWriteTimeUtc(_store.FilePath).Ticks}|{active}|{CanvasPath}|{ProfileLabel(active)}|{DesktopWidgetWindow.WallpaperKey}|{area}|{VisualTreeHelper.GetDpi(this).DpiScaleX}";
        var documentChanged = _overviewKey != key;
        var restoringSaved = draft == null && _overviewHadDraft;
        if (draft == null && !documentChanged && !restoringSaved) return;
        _overviewKey = key;
        _overviewHadDraft = draft != null;
        _desktopOverview.Width = area.Width; _desktopOverview.Height = area.Height; _desktopOverview.ClipToBounds = true;
        _desktopOverview.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "BgBrush");
        if (documentChanged)
        {
            _desktopOverview.Children.Clear();
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var backdrop = new Border { Width = screen.Bounds.Width, Height = screen.Bounds.Height, BorderThickness = new Thickness(1) };
                backdrop.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
                backdrop.Background = CanvasWallpaperBrush() ?? DesktopWidgetWindow.WallpaperBrush(new Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height), screen.Bounds);
                System.Windows.Controls.Canvas.SetLeft(backdrop, screen.Bounds.Left - area.Left);
                System.Windows.Controls.Canvas.SetTop(backdrop, screen.Bounds.Top - area.Top); _desktopOverview.Children.Add(backdrop);
            }
        }
        var visibleIds = widgets.Select(widget => widget.Id).ToHashSet();
        foreach (var id in _canvasPreviews.Keys.Where(id => !visibleIds.Contains(id)).ToArray())
        {
            var removed = _canvasPreviews[id];
            _desktopOverview.Children.Remove(removed.Item); removed.Renderer.Close(); _canvasPreviews.Remove(id);
        }
        foreach (var widget in widgets)
        {
            // Retain the visual tree so typing or dragging updates one widget rather than recreating the whole scene.
            if (!_canvasPreviews.TryGetValue(widget.Id, out var preview))
            {
                var renderer = new DesktopWidgetWindow(widget, _store);
                var content = (FrameworkElement)renderer.Content; renderer.Content = null;
                var item = new Border { Child = new Viewbox { Child = content, Stretch = Stretch.Fill }, Cursor = System.Windows.Input.Cursors.Hand };
                item.PreviewMouseLeftButtonDown += (sender, e) =>
                {
                    if (sender is Border { Tag: DesktopWidget current }) StartCanvasDrag(current, e);
                };
                preview = (renderer, item, content); _canvasPreviews.Add(widget.Id, preview);
            }
            if (documentChanged || restoringSaved || draft == null || widget.Id == draft.Id || preview.Item.Tag == null)
            {
                var bounds = preview.Renderer.UpdatePreview(widget, DesktopWidgetWindow.PreviewScale(widget));
                preview.Content.Width = widget.Width; preview.Content.Height = widget.Height;
                preview.Item.Width = bounds.Width; preview.Item.Height = bounds.Height;
                preview.Item.ToolTip = widget.DisplayTitle; preview.Item.Tag = widget;
                preview.Item.Opacity = document.Enabled ? 1 : .4;
                System.Windows.Controls.Canvas.SetLeft(preview.Item, bounds.X - area.Left);
                System.Windows.Controls.Canvas.SetTop(preview.Item, bounds.Y - area.Top);
            }
            if (!_desktopOverview.Children.Contains(preview.Item)) _desktopOverview.Children.Add(preview.Item);
        }
    }

    private void Add(DesktopWidgetKind kind)
    {
        if (!TryLeaveDraft()) return;
        var document = _store.Load();
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var offset = document.Widgets.Count % 8 * 28;
        Select(new DesktopWidget { Kind = kind, Title = kind == DesktopWidgetKind.NowPlaying ? "Now playing" : kind == DesktopWidgetKind.Note ? "A little reminder" : "New " + kind.ToString().ToLowerInvariant(),
            ProfileId = string.IsNullOrEmpty(CanvasPath) ? (_scopeFilter.SelectedItem as ProfileChoice)?.Id : CanvasProfile,
            WallpaperPath = EditableCanvasPath(),
            Content = kind == DesktopWidgetKind.Note ? "Make room for what matters." : "", FontSize = kind == DesktopWidgetKind.Clock ? 76 : kind == DesktopWidgetKind.NowPlaying ? 24 : kind == DesktopWidgetKind.Countdown ? 30 : 16,
            FontFamily = kind == DesktopWidgetKind.Clock ? ClockWidgetView.ScriptFont : "Segoe UI",
            Alignment = kind == DesktopWidgetKind.Clock ? WidgetTextAlignment.Center : WidgetTextAlignment.Left,
            Width = kind == DesktopWidgetKind.Clock ? 520 : kind == DesktopWidgetKind.NowPlaying ? 360 : 300, Height = kind == DesktopWidgetKind.Clock ? 310 : kind == DesktopWidgetKind.NowPlaying ? 140 : 230,
            MusicLayout = NowPlayingLayout.Minimal, ShowAlbumArt = kind != DesktopWidgetKind.NowPlaying, ShowPlaybackControls = kind != DesktopWidgetKind.NowPlaying,
            MusicAccentColor = "#F1F2EE",
            ShowHeader = kind is not (DesktopWidgetKind.NowPlaying or DesktopWidgetKind.Clock or DesktopWidgetKind.Visualizer), ShowBorder = kind is not (DesktopWidgetKind.NowPlaying or DesktopWidgetKind.Clock or DesktopWidgetKind.Visualizer), ShowBackground = kind is not (DesktopWidgetKind.NowPlaying or DesktopWidgetKind.Clock or DesktopWidgetKind.Visualizer),
            X = area.Right - 340 - offset, Y = area.Top + 70 + offset }, true);
        if (kind == DesktopWidgetKind.Link) _leetCodeUser.Text = "_Unkillable__Demon_King";
        _loading = true; _list.SelectedItem = null; _loading = false;
        _message.Text = "Unsaved widget";
    }
    private void Select(DesktopWidget widget, bool isNew)
    {
        var changedWidget = _selected?.Id != widget.Id;
        if (!changedWidget && IsDraftDirty) return;
        _loading = true;
        _selected = widget; _isNew = isNew;
        LoadSkin(widget);
        if (widget.ProfileId.HasValue && !_profile.Items.Cast<ProfileChoice>().Any(choice => choice.Id == widget.ProfileId))
            _profile.ItemsSource = _profile.Items.Cast<ProfileChoice>().Append(new ProfileChoice(widget.ProfileId, "Missing profile")).ToList();
        _profile.SelectedItem = _profile.Items.Cast<ProfileChoice>().First(choice => choice.Id == widget.ProfileId);
        _glassEffect.IsChecked = widget.GlassEffect;
        _fields.Visibility = Visibility.Visible; SetEditorVisible(true);
        if (changedWidget) SetEditorTab(0);
        _kindLabel.Text = (isNew ? "New " : "") + (widget.Kind == DesktopWidgetKind.NowPlaying ? "Now playing" : widget.Kind.ToString());
        _title.Text = widget.Title; _content.Text = widget.Content;
        _linkDescription.Text = widget.LinkCustomDescription; _leetCodeUser.Text = widget.LeetCodeUsername;
        _linkFields.Visibility = widget.Kind == DesktopWidgetKind.Link ? Visibility.Visible : Visibility.Collapsed;
        _musicFields.Visibility = widget.Kind == DesktopWidgetKind.NowPlaying ? Visibility.Visible : Visibility.Collapsed;
        _clockFields.Visibility = widget.Kind == DesktopWidgetKind.Clock ? Visibility.Visible : Visibility.Collapsed;
        _clock24Hour.IsChecked = widget.ClockUse24Hour; _clockGreeting.IsChecked = widget.ClockShowGreeting; _clockVisualizer.IsChecked = widget.ClockShowVisualizer;
        _musicLayout.SelectedItem = widget.MusicLayout; _albumArt.IsChecked = widget.ShowAlbumArt;
        _playbackControls.IsChecked = widget.ShowPlaybackControls; _playbackProgress.IsChecked = widget.ShowPlaybackProgress;
        _audioVisualizer.IsChecked = widget.ShowAudioVisualizer;
        _musicAccent.Text = widget.MusicAccentColor;
        _date.SelectedDate = widget.Target.LocalDateTime.Date;
        _time.Text = widget.Target.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        _width.Value = widget.Width; _height.Value = widget.Height;
        _loadedWidth = widget.Width; _loadedHeight = widget.Height;
        _font.Text = widget.FontFamily; _fontSize.Value = widget.FontSize;
        _alignment.SelectedItem = widget.Alignment; _imageFit.SelectedItem = widget.ImageFit;
        _textColor.Text = widget.TextColor; _backgroundColor.Text = widget.BackgroundColor;
        _borderColor.Text = widget.BorderColor; _borderWidth.Value = widget.BorderWidth;
        _italic.IsChecked = widget.Italic;
        _bold.IsChecked = widget.Bold; _opacity.Value = widget.Opacity * 100; _radius.Value = widget.CornerRadius;
        _showHeader.IsChecked = widget.ShowHeader; _showBorder.IsChecked = widget.ShowBorder;
        _showBackground.IsChecked = widget.ShowBackground;
        _enabled.IsChecked = widget.Enabled; _locked.IsChecked = widget.Locked;
        _targetFields.Visibility = widget.Kind == DesktopWidgetKind.Countdown ? Visibility.Visible : Visibility.Collapsed;
        _content.Visibility = widget.Kind is DesktopWidgetKind.Countdown or DesktopWidgetKind.Sketch or DesktopWidgetKind.NowPlaying or DesktopWidgetKind.Clock or DesktopWidgetKind.Visualizer ? Visibility.Collapsed : Visibility.Visible;
        _contentLabel.Visibility = _content.Visibility;
        _contentLabel.Text = widget.Kind switch { DesktopWidgetKind.Image => "Image file", DesktopWidgetKind.Link => "Website address (https://…)", _ => "Your note" };
        _browse.Visibility = widget.Kind == DesktopWidgetKind.Image ? Visibility.Visible : Visibility.Collapsed;
        _content.AcceptsReturn = widget.Kind == DesktopWidgetKind.Note;
        _content.MinHeight = widget.Kind == DesktopWidgetKind.Note ? 110 : 36;
        _delete.IsEnabled = !isNew;
        _duplicate.IsEnabled = !isNew; _save.IsEnabled = true;
        _save.Content = isNew ? "Create" : "Save";
        _imageFit.Visibility = widget.Kind == DesktopWidgetKind.Image ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
        _savedDraft = DraftSnapshot();
        RefreshPreview();
    }
    private void Save()
    {
        if (_selected == null) return;
        var title = _title.Text.Trim();
        var content = _selected.Kind == DesktopWidgetKind.Note ? _content.Text : _content.Text.Trim();
        if (_selected.Kind == DesktopWidgetKind.Link && !DesktopWidget.TryGetLink(content, out _))
            throw new InvalidOperationException("Enter a complete http:// or https:// website address.");
        if (_selected.Kind == DesktopWidgetKind.Image && (!File.Exists(content) || !WallpaperEngine.IsImageFile(content)))
            throw new InvalidOperationException("Choose an existing image file.");
        if (!SceneController.TryParseAccent(_textColor.Text.Trim(), out _) || !SceneController.TryParseAccent(_backgroundColor.Text.Trim(), out _)
            || !SceneController.TryParseAccent(_borderColor.Text.Trim(), out _))
            throw new InvalidOperationException("Use #RRGGBB for text and background colors, or choose a color.");
        if (_selected.Kind == DesktopWidgetKind.NowPlaying && !SceneController.TryParseAccent(_musicAccent.Text.Trim(), out _))
            throw new InvalidOperationException("Use #RRGGBB for the playback accent, or choose a color.");
        var target = _selected.Target;
        if (_selected.Kind == DesktopWidgetKind.Countdown)
        {
            if (_date.SelectedDate == null || !TimeOnly.TryParseExact(_time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new InvalidOperationException("Choose a date and enter the time as HH:mm.");
            var local = DateTime.SpecifyKind(_date.SelectedDate.Value.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(local)) throw new InvalidOperationException("That time is skipped by daylight saving. Choose another time.");
            target = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }
        _store.Update(document =>
        {
            var saved = document.Widgets.FirstOrDefault(w => w.Id == _selected.Id);
            if (saved == null && !_isNew) throw new InvalidOperationException("This widget was removed. Add another widget.");
            if (saved == null) { saved = _selected; document.Widgets.Add(saved); document.Enabled = true; }
            if (saved.Content != content || saved.LeetCodeUsername != _leetCodeUser.Text.Trim())
            {
                saved.LinkUpdatedAt = null; saved.LinkPageTitle = ""; saved.LinkDescription = ""; saved.LinkStatus = "";
                saved.PotdCompletions.Clear(); saved.ActivityStreak = 0; saved.PotdDate = null; saved.PotdTitle = ""; saved.PotdUrl = "";
            }
            saved.Title = title; saved.Content = content; saved.Target = target;
            saved.ProfileId = (_profile.SelectedItem as ProfileChoice)?.Id;
            if (!saved.ProfileId.HasValue) saved.WallpaperPath = "";
            document.ActiveProfileId = _activeProfile();
            document.ActiveWallpaperPath = _activeWallpaper();
            saved.LinkCustomDescription = _linkDescription.Text.Trim(); saved.LeetCodeUsername = _leetCodeUser.Text.Trim();
            if (_width.Value != _loadedWidth) saved.Width = _width.Value;
            if (_height.Value != _loadedHeight) saved.Height = _height.Value;
            saved.Enabled = _enabled.IsChecked == true; saved.Locked = _locked.IsChecked == true;
            ReadAppearance(saved);
            ReadPosition(saved, changedOnly: true);
            if (saved.Enabled) document.Enabled = true;
        });
        _isNew = false;
        _savedDraft = DraftSnapshot();
        RefreshList();
        _startHost();
        _message.Text = _enabled.IsChecked == true ? "Saved." : "Saved as hidden.";
    }
    private void Delete()
    {
        if (_selected == null || _isNew) return;
        if (!(ConfirmDeleteAction?.Invoke() ?? ConfirmWidgetDeletion())) return;
        _store.Update(document => document.Widgets.RemoveAll(w => w.Id == _selected.Id));
        _selected = null; _isNew = false;
        CloseEditor();
        RefreshList();
    }
    private bool ConfirmWidgetDeletion()
    {
        var owner = Window.GetWindow(this);
        var dialog = new Window
        {
            Title = "Delete widget", Width = 430, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, Owner = owner, ShowInTaskbar = false,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            FontSize = 14
        };
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = "Delete this widget?", FontSize = 22, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        var description = new TextBlock
        {
            Text = "This widget will be removed from your desktop canvas.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 24)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        content.Children.Add(description);
        var commands = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 82, MinHeight = 36, IsCancel = true, IsDefault = true };
        var delete = new Button { Content = "Delete widget", MinWidth = 110, MinHeight = 36, Margin = new Thickness(8, 0, 0, 0) };
        delete.SetResourceReference(StyleProperty, "DangerButton");
        delete.Click += (_, _) => dialog.DialogResult = true;
        commands.Children.Add(cancel);
        commands.Children.Add(delete);
        content.Children.Add(commands);
        UiAppearance.SetDialogContent(dialog, content, "Delete widget");
        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }
    private void ShowWidgets()
    {
        _store.Update(document =>
        {
            if (!document.Widgets.Any(w => w.Enabled)) throw new InvalidOperationException("Add a widget or enable an existing one first.");
            document.Enabled = true;
        });
        _startHost(); RefreshStatus();
    }
    private void FindWidgets()
    {
        _store.Update(document =>
        {
            if (!document.Widgets.Any(w => w.Enabled)) throw new InvalidOperationException("Create or enable a widget first.");
            document.Enabled = true;
            var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var index = 0;
            foreach (var widget in document.Widgets.Where(w => w.Enabled))
            {
                var offset = index++ % 8 * 28;
                widget.X = area.Right - 340 - offset; widget.Y = area.Top + 70 + offset;
            }
        });
        _startHost();
        _message.Text = "Moved into view.";
    }
    private void Duplicate()
    {
        if (_selected == null || _isNew) return;
        DesktopWidget? copy = null;
        _store.Update(document =>
        {
            var original = document.Widgets.FirstOrDefault(w => w.Id == _selected.Id);
            if (original == null) throw new InvalidOperationException("This widget was removed.");
            copy = original.Duplicate(); document.Widgets.Add(copy);
            document.Enabled = true;
        });
        _selected = copy; RefreshList(); _startHost();
    }
    private void HideWidgets()
    {
        _store.Update(document => document.Enabled = false);
        RefreshStatus();
    }
    private async void RefreshLink()
    {
        if (_selected?.Kind != DesktopWidgetKind.Link) return;
        try
        {
            Save(); var id = _selected.Id; var widget = _store.Load().Widgets.First(item => item.Id == id);
            _message.Text = "Fetching public link details…";
            await DesktopLinkService.RefreshAsync(_store, widget);
            if (_selected?.Id == id) { RefreshList(); _message.Text = "Link details refreshed."; }
        }
        catch (Exception ex) { _message.Text = ex.Message; }
    }
}
