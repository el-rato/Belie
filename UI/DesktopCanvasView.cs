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

internal sealed class DesktopCanvasView : UserControl
{
    private readonly DesktopCanvasStore _store;
    private readonly Action _startHost;
    private readonly Func<IReadOnlyList<WallpaperProfile>> _profiles;
    private readonly Func<Guid?> _activeProfile;
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
    private readonly ComboBox _musicLayout = new() { ItemsSource = Enum.GetValues<NowPlayingLayout>() };
    private readonly CheckBox _albumArt = new() { Content = "Show album art" };
    private readonly CheckBox _playbackControls = new() { Content = "Show playback controls" };
    private readonly CheckBox _playbackProgress = new() { Content = "Show progress and time" };
    private readonly TextBox _musicAccent = new() { MaxLength = 7 };
    private readonly StackPanel _contentPage = new(), _stylePage = new(), _layoutPage = new();
    private readonly ScrollViewer _editorScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Border _editorFrame = new();
    private readonly ColumnDefinition _editorColumn = new() { Width = new GridLength(340) };
    private readonly System.Windows.Controls.Primitives.Popup _addPopup = new() { StaysOpen = false, AllowsTransparency = true, PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade };
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
        Progress = _playbackProgress.IsChecked, MusicAccent = _musicAccent.Text });
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
        Func<IReadOnlyList<WallpaperProfile>>? profiles = null, Func<Guid?>? activeProfile = null)
    {
        _store = store ?? new DesktopCanvasStore(DesktopCanvasProcess.DefaultFile);
        _startHost = startHost ?? (() => DesktopCanvasProcess.Start(_store.FilePath));
        _profiles = profiles ?? (() => Array.Empty<WallpaperProfile>());
        _activeProfile = activeProfile ?? (() => _store.Load().ActiveProfileId);
        NameScope.SetNameScope(this, new NameScope());
        Content = BuildWorkspace();
        foreach (var preview in new FrameworkElement[] { _preview, _list, _desktopOverview })
            TextOptions.SetTextRenderingMode(preview, TextRenderingMode.Grayscale);
        SizeChanged += (_, _) => _preview.Height = ActualHeight < 640 ? 96 : Math.Min(300, 140 + (ActualHeight - 640) * .4);
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
        Unloaded += (_, _) => { _timer.Stop(); _addPopup.IsOpen = false; };
        Loaded += (_, _) => { RefreshProfiles(); Run(RefreshList); RefreshStatus(); _timer.Start(); };
        _fields.Visibility = Visibility.Collapsed;
        _save.IsEnabled = false; _duplicate.IsEnabled = false; _delete.IsEnabled = false;
        foreach (var box in new[] { _title, _content, _textColor, _backgroundColor, _borderColor, _time, _linkDescription, _leetCodeUser, _musicAccent }) box.TextChanged += (_, _) => RefreshPreview();
        _musicLayout.SelectionChanged += (_, _) => RefreshPreview();
        foreach (var option in new[] { _albumArt, _playbackControls, _playbackProgress })
        { option.Checked += (_, _) => RefreshPreview(); option.Unchecked += (_, _) => RefreshPreview(); }
        foreach (var (name, element) in new (string, FrameworkElement)[] { ("WidgetMusicLayout", _musicLayout), ("WidgetAlbumArt", _albumArt),
            ("WidgetPlaybackControls", _playbackControls), ("WidgetPlaybackProgress", _playbackProgress), ("WidgetMusicAccent", _musicAccent) })
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
        RefreshProfiles(); Run(RefreshList);
    }

    private FrameworkElement BuildWorkspace()
    {
        var workspace = new Grid { Margin = new Thickness(24) };
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition());
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headerFrame = new Border { Style = (Style)FindResource("WorkspaceBanner"), Margin = new Thickness(0, 0, 0, 18) };
        var header = new Grid(); headerFrame.Child = header;
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Widgets", Style = (Style)FindResource("WorkspaceHeading") });
        var subtitle = new TextBlock { Text = "Add, arrange, and customize your desktop widgets.", Style = (Style)FindResource("WorkspaceCaption") };
        heading.Children.Add(subtitle);
        var quickAdd = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var kind in new[] { DesktopWidgetKind.Note, DesktopWidgetKind.Image, DesktopWidgetKind.Countdown, DesktopWidgetKind.NowPlaying })
        {
            var captured = kind;
            var quick = ActionButton("+ " + (kind == DesktopWidgetKind.NowPlaying ? "Now playing" : kind.ToString()), "QuickAdd" + kind + "Button", () => Add(captured));
            quick.Padding = new Thickness(12, 9, 12, 9); quick.FontSize = 12;
            quick.SetResourceReference(Control.BackgroundProperty, "SurfaceRaisedBrush");
            var glyph = kind switch { DesktopWidgetKind.Note => "\uE70B", DesktopWidgetKind.Image => "\uEB9F", DesktopWidgetKind.Countdown => "\uE916", _ => "\uE8D6" };
            var quickContent = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var icon = new TextBlock { Text = glyph, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            quickContent.Children.Add(icon);
            quickContent.Children.Add(new TextBlock { Text = kind == DesktopWidgetKind.NowPlaying ? "Now playing" : kind.ToString(), VerticalAlignment = VerticalAlignment.Center });
            quick.Content = quickContent;
            quickAdd.Children.Add(quick);
        }
        heading.Children.Add(quickAdd); header.Children.Add(heading);
        var add = ActionButton("+  Add widget", "AddWidgetButton", () => _addPopup.IsOpen = !_addPopup.IsOpen);
        add.SetResourceReference(StyleProperty, "PrimaryButton"); add.VerticalAlignment = VerticalAlignment.Center; add.Margin = new Thickness(0);
        Grid.SetColumn(add, 1); header.Children.Add(add); workspace.Children.Add(headerFrame);
        _addPopup.PlacementTarget = add; _addPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        var picker = new StackPanel();
        foreach (var kind in Enum.GetValues<DesktopWidgetKind>())
        {
            var captured = kind;
            var label = kind == DesktopWidgetKind.NowPlaying ? "Now playing" : kind.ToString();
            var choice = ActionButton(label, "Add" + kind + "Button", () => { _addPopup.IsOpen = false; Add(captured); });
            choice.Content = new TextBlock { Text = label, FontSize = 14 }; choice.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            choice.Margin = new Thickness(0, 2, 0, 2); choice.BorderThickness = new Thickness(0); choice.MinWidth = 170; picker.Children.Add(choice);
        }
        var pickerFrame = new Border { Child = picker, CornerRadius = new CornerRadius(12), Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        pickerFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush"); pickerFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _addPopup.Child = pickerFrame; workspace.Children.Add(_addPopup); RegisterName("WidgetTypePicker", _addPopup);

        var body = new Grid(); Grid.SetRow(body, 2); workspace.Children.Add(body);
        body.ColumnDefinitions.Add(_galleryColumn); body.ColumnDefinitions.Add(_editorColumn);
        var gallery = new Grid { Margin = new Thickness(0, 0, 16, 0) }; body.Children.Add(gallery);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var actions = new WrapPanel { MaxWidth = 270 };
        _showAll = ActionButton("Show widgets", "ShowWidgetsButton", ShowWidgets); _hideAll = ActionButton("Hide widgets", "HideWidgetsButton", HideWidgets);
        actions.Children.Add(_showAll); actions.Children.Add(_hideAll);
        var find = ActionButton("Find widgets", "FindWidgetsButton", FindWidgets); find.ToolTip = "Bring widgets into view"; find.FontSize = 12; find.Padding = new Thickness(8, 2, 8, 2); actions.Children.Add(find);
        DockPanel.SetDock(actions, Dock.Right); toolbar.Children.Add(actions);
        DockPanel.SetDock(_scopeFilter, Dock.Left); toolbar.Children.Add(_scopeFilter);
        _status.Margin = new Thickness(0); _status.FontSize = 12; _status.VerticalAlignment = VerticalAlignment.Center;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); toolbar.Children.Add(_status); Grid.SetRow(toolbar, 1); workspace.Children.Add(toolbar);
        var galleryArea = new Grid(); gallery.Children.Add(galleryArea);
        _list.Background = System.Windows.Media.Brushes.Transparent; _list.BorderThickness = new Thickness(0); _list.Padding = new Thickness(0);
        _list.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
        _list.ItemContainerStyle = GalleryItemStyle(); _list.DisplayMemberPath = ""; _list.ItemTemplate = GalleryCardTemplate(); galleryArea.Children.Add(_list);
        _empty.Text = "Drop an image, link, or note here.\nOr add your first widget."; _empty.FontSize = 15; _empty.TextAlignment = TextAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center; _empty.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); _empty.IsHitTestVisible = false; galleryArea.Children.Add(_empty);

        _overviewFrame.CornerRadius = new CornerRadius(16); _overviewFrame.Padding = new Thickness(18);
        _overviewFrame.BorderThickness = new Thickness(1);
        _overviewFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        _overviewFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var overview = new Grid(); _overviewFrame.Child = overview;
        overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); overview.RowDefinitions.Add(new RowDefinition()); overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var overviewHeading = new StackPanel();
        overviewHeading.Children.Add(new TextBlock { Text = "Desktop preview", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        _overviewCaption.Margin = new Thickness(0, 6, 0, 14); _overviewCaption.TextWrapping = TextWrapping.Wrap;
        _overviewCaption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); overviewHeading.Children.Add(_overviewCaption); overview.Children.Add(overviewHeading);
        var desktopPreview = new Viewbox { Child = _desktopOverview, Stretch = Stretch.Uniform };
        Grid.SetRow(desktopPreview, 1); overview.Children.Add(desktopPreview);
        var overviewHint = new TextBlock { Text = "Select a widget to edit its content, style, and position.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), FontSize = 12 };
        Grid.SetRow(overviewHint, 2); overview.Children.Add(overviewHint);
        Grid.SetColumn(_overviewFrame, 1); body.Children.Add(_overviewFrame);

        _editorFrame.CornerRadius = new CornerRadius(16); _editorFrame.Padding = new Thickness(16); _editorFrame.BorderThickness = new Thickness(1);
        _editorFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); _editorFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Grid.SetColumn(_editorFrame, 1); body.Children.Add(_editorFrame);
        var editor = new Grid(); _editorFrame.Child = editor;
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) editor.RowDefinitions.Add(new RowDefinition { Height = height });
        var editorHeader = new DockPanel(); _kindLabel.Margin = new Thickness(0); _kindLabel.VerticalAlignment = VerticalAlignment.Center;
        var close = ActionButton("Close", "CloseWidgetEditorButton", CloseEditor); close.ToolTip = "Close editor"; close.Padding = new Thickness(8, 2, 8, 2); close.Margin = new Thickness(0); close.FontSize = 12;
        DockPanel.SetDock(close, Dock.Right); editorHeader.Children.Add(close); editorHeader.Children.Add(_kindLabel); editor.Children.Add(editorHeader);
        Grid.SetRow(_preview, 1); editor.Children.Add(_preview);
        var tabs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (label, index) in new[] { ("Content", 0), ("Style", 1), ("Layout", 2) })
        {
            var tab = ActionButton(label, "Widget" + label + "Tab", () => SetEditorTab(index)); tab.Margin = new Thickness(0, 0, 4, 0); tab.BorderThickness = new Thickness(0); _tabButtons.Add(tab); tabs.Children.Add(tab);
        }
        Grid.SetRow(tabs, 2); editor.Children.Add(tabs);
        _fields.Children.Add(_contentPage); _fields.Children.Add(_stylePage); _fields.Children.Add(_layoutPage);
        Field(_contentPage, "Title · optional", _title);
        Field(_contentPage, "Show on", _profile);
        _contentLabel.Margin = new Thickness(0, 12, 0, 6); _contentPage.Children.Add(_contentLabel); _contentPage.Children.Add(_content); _contentPage.Children.Add(_browse);
        Field(_linkFields, "Description · optional", _linkDescription); Field(_linkFields, "LeetCode username", _leetCodeUser);
        _linkFields.Children.Add(ActionButton("Refresh details", "RefreshLinkButton", RefreshLink)); _contentPage.Children.Add(_linkFields);
        _musicFields.Children.Add(new TextBlock { Text = "Music players only, such as Spotify, Apple Music, and TIDAL. Browser playback and videos are ignored. Drag the song text or album art to move the widget.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 12 });
        Field(_musicFields, "Player layout", _musicLayout);
        foreach (var option in new[] { _albumArt, _playbackControls, _playbackProgress }) { option.Margin = new Thickness(0, 10, 0, 0); _musicFields.Children.Add(option); }
        ColorField("Playback accent", _musicAccent, _musicFields);
        _contentPage.Children.Add(_musicFields);
        Field(_targetFields, "Date", _date); Field(_targetFields, "Time · HH:mm", _time); _contentPage.Children.Add(_targetFields);
        var presets = new WrapPanel();
        foreach (var name in new[] { "Rainmeter", "Glass", "Paper", "Minimal", "Gothic", "Cathedral", "Crimson", "Parchment", "Royal", "Neon", "Terminal" })
        {
            var preset = name; var chip = ActionButton(preset, "Style" + preset + "Button", () => ApplyStyle(preset)); chip.Margin = new Thickness(0, 0, 6, 6); chip.FontSize = 12; presets.Children.Add(chip);
        }
        _stylePage.Children.Add(presets); _glassEffect.Content = "Glass finish"; _stylePage.Children.Add(_glassEffect);
        Field(_stylePage, "Font", _font); Field(_stylePage, "Text size", _fontSize); Field(_stylePage, "Alignment", _alignment);
        var emphasis = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _bold.Content = "Bold"; _italic.Content = "Italic"; _italic.Margin = new Thickness(16, 0, 0, 0); emphasis.Children.Add(_bold); emphasis.Children.Add(_italic); _stylePage.Children.Add(emphasis);
        ColorField("Text", _textColor, _stylePage); ColorField("Background", _backgroundColor, _stylePage); ColorField("Border", _borderColor, _stylePage);
        Field(_stylePage, "Border width", _borderWidth); Field(_stylePage, "Opacity", _opacity); Field(_stylePage, "Corners", _radius);
        foreach (var option in new[] { _showHeader, _showBorder, _showBackground }) { option.Margin = new Thickness(0, 8, 0, 0); _stylePage.Children.Add(option); }
        Field(_layoutPage, "Width", _width); Field(_layoutPage, "Height", _height); Field(_layoutPage, "Image fit", _imageFit);
        _layoutPage.Children.Add(_enabled); _layoutPage.Children.Add(_locked);
        _editorScroll.Content = _fields; Grid.SetRow(_editorScroll, 3); editor.Children.Add(_editorScroll); RegisterName("WidgetEditorScroll", _editorScroll);
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _save = ActionButton("Create", "SaveWidgetButton", Save); _save.SetResourceReference(StyleProperty, "PrimaryButton"); footer.Children.Add(_save);
        _duplicate.Content = "Duplicate"; _duplicate.ToolTip = "Duplicate widget"; _duplicate.Padding = new Thickness(10, 6, 10, 6); _duplicate.Margin = new Thickness(0, 0, 6, 0);
        _duplicate.Click += (_, _) => Run(Duplicate); Grid.SetColumn(_duplicate, 1); footer.Children.Add(_duplicate); RegisterName("DuplicateWidgetButton", _duplicate);
        _delete.Content = "Delete widget"; _delete.SetResourceReference(StyleProperty, "DangerButton"); _delete.ToolTip = "Delete widget"; _delete.Padding = new Thickness(10, 6, 10, 6); _delete.Margin = new Thickness(0);
        _delete.Click += (_, _) => Run(Delete); Grid.SetColumn(_delete, 2); footer.Children.Add(_delete); RegisterName("DeleteWidgetButton", _delete);
        Grid.SetRow(footer, 4); editor.Children.Add(footer);
        _message.Margin = new Thickness(0, 10, 0, 0); Grid.SetRow(_message, 3); workspace.Children.Add(_message);
        SetEditorTab(0); SetEditorVisible(false); return workspace;
    }

    private void SetEditorTab(int index)
    {
        var pages = new[] { _contentPage, _stylePage, _layoutPage };
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            _tabButtons[i].SetResourceReference(Control.BackgroundProperty, i == index ? "AccentSubtleBrush" : "SurfaceRaisedBrush");
            _tabButtons[i].SetResourceReference(Control.ForegroundProperty, i == index ? "AccentTextBrush" : "TextSecondaryBrush");
        }
        _editorScroll.ScrollToTop();
    }
    private void SetEditorVisible(bool visible)
    {
        _editorFrame.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _overviewFrame.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        _editorColumn.Width = new GridLength(1, GridUnitType.Star);
    }
    private void CloseEditor()
    {
        if (!TryLeaveDraft()) return;
        _loading = true; _selected = null; _list.SelectedItem = null; _loading = false; _fields.Visibility = Visibility.Collapsed; SetEditorVisible(false);
        _preview.Child = null; _previewWindow?.Close(); _previewWindow = null;
        _save.IsEnabled = _delete.IsEnabled = _duplicate.IsEnabled = false;
        RefreshOverview(_store.Load());
    }

    private static string WidgetGlyph(DesktopWidgetKind kind) => kind switch
    { DesktopWidgetKind.Note => "✎", DesktopWidgetKind.Countdown => "◷", DesktopWidgetKind.Image => "▧", DesktopWidgetKind.Link => "↗", _ => "〰" };

    private static Style GalleryItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 12, 12)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "Card";
        frame.SetResourceReference(StyleProperty, "InteractiveCard");
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        frame.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        frame.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = frame };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceHoverBrush"), "Card"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "Card")); template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"), "Card"));
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("AccentSubtleBrush"), "Card")); template.Triggers.Add(selected);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
    private DataTemplate GalleryCardTemplate()
    {
        var content = new FrameworkElementFactory(typeof(ContentControl));
        content.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding { Converter = new GalleryConverter(BuildGalleryCard) });
        return new DataTemplate { VisualTree = content };
    }
    private sealed class GalleryConverter(Func<DesktopWidget, FrameworkElement> render) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is DesktopWidget widget ? render(widget) : DependencyProperty.UnsetValue;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private FrameworkElement BuildGalleryCard(DesktopWidget widget)
    {
        var panel = new StackPanel { Width = 152 };
        var renderer = new DesktopWidgetWindow(widget, _store);
        renderer.UpdatePreview(widget, DesktopWidgetWindow.PreviewScale(widget));
        var visual = (FrameworkElement)renderer.Content; renderer.Content = null;
        visual.Width = widget.Width; visual.Height = widget.Height; renderer.Close();
        var preview = new Viewbox { Height = 106, Stretch = Stretch.Uniform, Child = visual, IsHitTestVisible = false };
        var previewFrame = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Child = preview };
        previewFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        panel.Children.Add(previewFrame);
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var toggle = new Button { Name = "CardVisibilityButton", Content = widget.Enabled ? "Hide" : "Show", FontSize = 12, Padding = new Thickness(6, 4, 6, 4), BorderThickness = new Thickness(0),
            ToolTip = widget.Enabled ? "Hide widget" : "Show widget" };
        System.Windows.Automation.AutomationProperties.SetName(toggle, widget.Enabled ? "Hide " + widget.DisplayTitle : "Show " + widget.DisplayTitle);
        toggle.SetResourceReference(Control.ForegroundProperty, widget.Enabled ? "AccentTextBrush" : "TextTertiaryBrush");
        toggle.Click += (_, e) => { e.Handled = true; Run(() => ToggleWidget(widget)); };
        DockPanel.SetDock(toggle, Dock.Right); row.Children.Add(toggle);
        row.Children.Add(new TextBlock { Text = widget.DisplayTitle, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); panel.Children.Add(row);
        var caption = new TextBlock { Text = (widget.Kind == DesktopWidgetKind.NowPlaying ? "Now playing" : widget.Kind.ToString()) + (widget.Enabled ? "" : " · Hidden"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); panel.Children.Add(caption);
        var scope = new TextBlock { Text = ProfileLabel(widget.ProfileId), FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        scope.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush"); panel.Children.Add(scope); return panel;
    }
    private void ToggleWidget(DesktopWidget widget)
    {
        var visible = false;
        _store.Update(document =>
        {
            var saved = document.Widgets.FirstOrDefault(item => item.Id == widget.Id);
            if (saved == null) return;
            visible = saved.Enabled = !saved.Enabled;
            if (visible) document.Enabled = true;
        });
        RefreshList(); if (visible) _startHost();
    }

    private Button ActionButton(string text, string name, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 8, 10, 8) };
        button.Click += (_, _) => Run(action);
        RegisterName(name, button);
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        return button;
    }
    private static void Field(StackPanel panel, string label, FrameworkElement control)
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 6), FontSize = 12 };
        if (control is Slider slider)
        { caption.Text = $"{label} · {slider.Value:0.#}"; slider.ValueChanged += (_, _) => caption.Text = $"{label} · {slider.Value:0.#}"; }
        panel.Children.Add(caption);
        panel.Children.Add(control);
    }
    internal void CreateFromDrop(System.Windows.IDataObject data)
    {
        var widgets = DesktopCanvasFeatures.ReadDrop(data);
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        DesktopCanvasFeatures.SaveDrop(_store, widgets, area.Right - 340, area.Top + 70);
        _selected = widgets[0]; RefreshList(); _startHost(); _message.Text = $"Created {widgets.Count} widget(s).";
    }
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
        widget.MusicAccentColor = _musicAccent.Text.Trim().ToUpperInvariant();
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
        var draft = _selected.Duplicate(); draft.Id = _selected.Id;
        draft.Title = _title.Text; draft.Content = _content.Text;
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
            if (_selected?.Kind == DesktopWidgetKind.NowPlaying)
            {
                _musicLayout.SelectedItem = NowPlayingLayout.Minimal;
                _albumArt.IsChecked = _playbackControls.IsChecked = false; _playbackProgress.IsChecked = true;
                _fontSize.Value = 24; _width.Value = 360; _height.Value = 140;
                _musicAccent.Text = _textColor.Text;
            }
        }
        _loading = false; RefreshPreview();
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
            var visible = document.Enabled ? document.Widgets.Count(w => w.IsVisibleOn(_activeProfile())) : 0;
            _status.Text = count == 0 ? "No widgets yet" : $"{visible} on desktop · {count} {(count == 1 ? "widget" : "widgets")} in gallery";
            _showAll.Visibility = document.Enabled ? Visibility.Collapsed : Visibility.Visible;
            _hideAll.Visibility = document.Enabled ? Visibility.Visible : Visibility.Collapsed;
            if (_overviewFrame.Visibility == Visibility.Visible) RefreshOverview(document);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void RefreshList()
    {
        var selected = _selected?.Id;
        var document = _store.Load();
        var filter = _scopeFilter.SelectedItem as ProfileChoice;
        var widgets = document.Widgets.Where(w => filter == null || filter.All || w.ProfileId == filter.Id).ToList();
        var loading = _loading; _loading = true;
        _list.ItemsSource = widgets;
        _list.SelectedItem = widgets.FirstOrDefault(w => w.Id == selected);
        _loading = loading;
        if (!loading && !IsDraftDirty && _list.SelectedItem is DesktopWidget current) Select(current, false);
        _empty.Text = document.Widgets.Count == 0 ? "Drop an image, link, or note here.\nOr add your first widget." : "No widgets in this group.";
        _empty.Visibility = widgets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshStatus();
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
        }
        RefreshStatus();
    }

    private void RefreshOverview(DesktopCanvasDocument document)
    {
        var active = _activeProfile();
        var widgets = document.Widgets.Where(widget => widget.IsVisibleOn(active)).ToArray();
        _overviewCaption.Text = "Preview: " + (active.HasValue ? ProfileLabel(active) : "Global widgets")
            + (document.Enabled ? " · includes global widgets" : " · widgets hidden")
            + (widgets.Length == 0 ? "\nNo widgets assigned here. Other profiles' widgets remain in the gallery." : "");
        var area = System.Windows.Forms.SystemInformation.VirtualScreen;
        var key = $"{File.GetLastWriteTimeUtc(_store.FilePath).Ticks}|{active}|{ProfileLabel(active)}|{DesktopWidgetWindow.WallpaperKey}|{area}|{VisualTreeHelper.GetDpi(this).DpiScaleX}";
        if (_overviewKey == key) return;
        _overviewKey = key;
        _desktopOverview.Width = area.Width; _desktopOverview.Height = area.Height; _desktopOverview.ClipToBounds = true;
        _desktopOverview.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "BgBrush");
        _desktopOverview.Children.Clear();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var backdrop = new Border { Width = screen.Bounds.Width, Height = screen.Bounds.Height, BorderThickness = new Thickness(1) };
            backdrop.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
            backdrop.Background = DesktopWidgetWindow.WallpaperBrush(new Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height), screen.Bounds);
            System.Windows.Controls.Canvas.SetLeft(backdrop, screen.Bounds.Left - area.Left);
            System.Windows.Controls.Canvas.SetTop(backdrop, screen.Bounds.Top - area.Top); _desktopOverview.Children.Add(backdrop);
        }
        foreach (var widget in widgets)
        {
            var renderer = new DesktopWidgetWindow(widget, _store);
            var bounds = renderer.UpdatePreview(widget, DesktopWidgetWindow.PreviewScale(widget));
            var content = (FrameworkElement)renderer.Content; renderer.Content = null; renderer.Close();
            content.Width = widget.Width; content.Height = widget.Height;
            var item = new Border { Width = bounds.Width, Height = bounds.Height, Child = new Viewbox { Child = content, Stretch = Stretch.Fill }, Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = widget.DisplayTitle, Opacity = document.Enabled ? 1 : .4 };
            item.PreviewMouseLeftButtonDown += (_, e) => { _list.SelectedItem = widget; e.Handled = true; };
            System.Windows.Controls.Canvas.SetLeft(item, bounds.X - area.Left); System.Windows.Controls.Canvas.SetTop(item, bounds.Y - area.Top);
            _desktopOverview.Children.Add(item);
        }
    }
    private void Add(DesktopWidgetKind kind)
    {
        if (!TryLeaveDraft()) return;
        var document = _store.Load();
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var offset = document.Widgets.Count % 8 * 28;
        Select(new DesktopWidget { Kind = kind, Title = kind == DesktopWidgetKind.NowPlaying ? "Now playing" : kind == DesktopWidgetKind.Note ? "A little reminder" : "New " + kind.ToString().ToLowerInvariant(),
            ProfileId = (_scopeFilter.SelectedItem as ProfileChoice)?.Id,
            Content = kind == DesktopWidgetKind.Note ? "Make room for what matters." : "", FontSize = kind == DesktopWidgetKind.NowPlaying ? 24 : kind == DesktopWidgetKind.Countdown ? 30 : 16,
            Width = kind == DesktopWidgetKind.NowPlaying ? 360 : 300, Height = kind == DesktopWidgetKind.NowPlaying ? 140 : 230,
            MusicLayout = NowPlayingLayout.Minimal, ShowAlbumArt = kind != DesktopWidgetKind.NowPlaying, ShowPlaybackControls = kind != DesktopWidgetKind.NowPlaying,
            MusicAccentColor = "#F1F2EE",
            ShowHeader = kind != DesktopWidgetKind.NowPlaying, ShowBorder = kind != DesktopWidgetKind.NowPlaying, ShowBackground = kind != DesktopWidgetKind.NowPlaying,
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
        _musicLayout.SelectedItem = widget.MusicLayout; _albumArt.IsChecked = widget.ShowAlbumArt;
        _playbackControls.IsChecked = widget.ShowPlaybackControls; _playbackProgress.IsChecked = widget.ShowPlaybackProgress;
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
        _content.Visibility = widget.Kind is DesktopWidgetKind.Countdown or DesktopWidgetKind.Sketch or DesktopWidgetKind.NowPlaying ? Visibility.Collapsed : Visibility.Visible;
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
            document.ActiveProfileId = _activeProfile();
            saved.LinkCustomDescription = _linkDescription.Text.Trim(); saved.LeetCodeUsername = _leetCodeUser.Text.Trim();
            if (_width.Value != _loadedWidth) saved.Width = _width.Value;
            if (_height.Value != _loadedHeight) saved.Height = _height.Value;
            saved.Enabled = _enabled.IsChecked == true; saved.Locked = _locked.IsChecked == true;
            ReadAppearance(saved);
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
        if (!(ConfirmDeleteAction?.Invoke() ?? (System.Windows.MessageBox.Show(Window.GetWindow(this), "Delete this widget?", "Delete widget", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes))) return;
        _store.Update(document => document.Widgets.RemoveAll(w => w.Id == _selected.Id));
        _selected = null; _isNew = false;
        CloseEditor();
        RefreshList();
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
