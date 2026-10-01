using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
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
    private readonly Viewbox _preview = new() { Height = 110, Stretch = Stretch.Uniform, IsHitTestVisible = false, Margin = new Thickness(0, 6, 0, 0) };
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
    private readonly ComboBox _boards = new() { DisplayMemberPath = nameof(DesktopBoard.Name), MinWidth = 170 };
    private readonly TextBox _boardName = new();
    private readonly TextBox _boardWallpaper = new();
    private readonly TextBox _boardTime = new();
    private readonly ComboBox _boardFit = new() { ItemsSource = Enum.GetValues<WallpaperProfiles.Models.FitMode>() };
    private readonly ComboBox _boardProfile = new() { DisplayMemberPath = nameof(WallpaperProfiles.Models.WallpaperProfile.Name) };
    private readonly ComboBox _wallpaperMode = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key", ItemsSource = new Dictionary<BoardWallpaperMode, string> {
        [BoardWallpaperMode.Normal] = "Normal wallpaper", [BoardWallpaperMode.Timed] = "Timed wallpaper",
        [BoardWallpaperMode.Rotating] = "Rotating wallpapers", [BoardWallpaperMode.RotatingTimed] = "Rotating + timed wallpaper" } };
    private readonly TextBox _rotationMinutes = new() { Text = "15" };
    private readonly CheckBox _rotationRandom = new() { Content = "Shuffle wallpapers" };
    private readonly TextBox _overrideWallpaper = new();
    private readonly TextBox _overrideStart = new() { Text = "20:00" };
    private readonly TextBox _overrideEnd = new() { Text = "08:00" };
    private readonly StackPanel _rotationFields = new();
    private readonly StackPanel _overrideFields = new();
    private readonly TextBlock _wallpaperSourceLabel = new();
    private readonly TextBlock _wallpaperHint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _widgetsStep = new();
    private readonly ScrollViewer _workflowScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private WallpaperProfiles.Models.WallpaperProfile? _boardScene;
    private readonly CheckBox _autoBoards = new() { Content = "Switch boards automatically at their daily local times" };
    private readonly StackPanel _linkFields = new();
    private readonly TextBox _linkDescription = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _leetCodeUser = new();
    private Guid? _activeBoard;
    private Guid? _editingBoard;
    private bool _loadingBoards;

    public DesktopCanvasView(DesktopCanvasStore? store = null, Action? startHost = null, IEnumerable<WallpaperProfiles.Models.WallpaperProfile>? profiles = null)
    {
        _store = store ?? new DesktopCanvasStore(DesktopCanvasProcess.DefaultFile);
        _startHost = startHost ?? (() => DesktopCanvasProcess.Start(_store.FilePath));
        NameScope.SetNameScope(this, new NameScope());
        _list.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        _list.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        _list.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 9, 10, 9)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
        var itemTemplate = new ControlTemplate(typeof(ListBoxItem));
        var itemBorder = new FrameworkElementFactory(typeof(Border));
        itemBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        itemBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        itemBorder.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        itemTemplate.VisualTree = itemBorder;
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentSubtleBrush")));
        itemStyle.Triggers.Add(selected);
        _list.ItemContainerStyle = itemStyle;
        _list.DisplayMemberPath = "";
        var itemContent = new FrameworkElementFactory(typeof(StackPanel));
        var itemTitle = new FrameworkElementFactory(typeof(TextBlock));
        itemTitle.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(DesktopWidget.DisplayTitle)));
        itemTitle.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        itemContent.AppendChild(itemTitle);
        var itemKind = new FrameworkElementFactory(typeof(TextBlock));
        itemKind.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(DesktopWidget.Kind)));
        itemKind.SetValue(TextBlock.FontSizeProperty, 11d);
        itemKind.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
        itemKind.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        itemContent.AppendChild(itemKind);
        _list.ItemTemplate = new DataTemplate { VisualTree = itemContent };
        var root = new StackPanel { Margin = new Thickness(32, 24, 32, 24), MaxWidth = 760 };
        root.Children.Add(new TextBlock { Text = "YOUR PERSONAL SPACE", FontSize = 10, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(new TextBlock { Text = "Desktop canvas", FontSize = 28, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "Keep ideas, moments, and useful things on your desktop.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 14) });
        var boardPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        Field(boardPanel, "Your board", _boards);
        var boardActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        boardActions.Children.Add(ActionButton("New board", "NewBoardButton", () => CreateBoard(false)));
        boardPanel.Children.Add(boardActions);
        var moreOptions = new StackPanel();
        Field(moreOptions, "Board name", _boardName);
        var manageBoards = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        manageBoards.Children.Add(ActionButton("Copy board", "CopyBoardButton", () => CreateBoard(true)));
        manageBoards.Children.Add(ActionButton("Midnight dark board", "DarkBoardButton", CreateDarkBoard));
        manageBoards.Children.Add(ActionButton("Delete board", "DeleteBoardButton", DeleteBoard));
        moreOptions.Children.Add(manageBoards);
        var boardOptions = new StackPanel();
        boardOptions.Children.Add(new TextBlock { Text = "Wallpaper", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
        Field(moreOptions, "Wallpaper mode", _wallpaperMode);
        _wallpaperMode.SelectionChanged += (_, _) => RefreshWallpaperFields();
        moreOptions.Children.Add(_wallpaperHint);
        Field(moreOptions, "Use an existing profile", _boardProfile);
        _boardProfile.ItemsSource = profiles?.ToList() ?? (store == null ? new ProfileStore(WallpaperProfiles.Infrastructure.AppPaths.ProfilesDir).LoadAll() : new List<WallpaperProfiles.Models.WallpaperProfile>());
        _boardProfile.SelectionChanged += (_, _) =>
        {
            if (_loadingBoards || _boardProfile.SelectedItem is not WallpaperProfiles.Models.WallpaperProfile profile) return;
            _boardScene = DesktopCanvasFeatures.CopyScene(profile); _boardWallpaper.Text = profile.FolderPath; _boardFit.SelectedItem = profile.FitMode;
            _wallpaperMode.SelectedValue = profile.SlideshowIntervalMinutes > 0 ? BoardWallpaperMode.Rotating : BoardWallpaperMode.Normal;
            _rotationMinutes.Text = Math.Max(1, profile.SlideshowIntervalMinutes).ToString(CultureInfo.InvariantCulture); _rotationRandom.IsChecked = profile.SlideshowRandom;
        };
        _wallpaperSourceLabel.Margin = new Thickness(0, 12, 0, 6); _wallpaperSourceLabel.FontSize = 12;
        boardOptions.Children.Add(_wallpaperSourceLabel); boardOptions.Children.Add(_boardWallpaper);
        Field(moreOptions, "Wallpaper fit", _boardFit);
        var wallpaperButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        wallpaperButtons.Children.Add(ActionButton("Choose wallpaper", "BoardWallpaperButton", () =>
        {
            var previous = _boardWallpaper.Text; ChooseWallpaper(_boardWallpaper);
            if (_boardWallpaper.Text != previous) _wallpaperMode.SelectedValue = SelectedWallpaperMode is BoardWallpaperMode.Timed or BoardWallpaperMode.RotatingTimed ? BoardWallpaperMode.Timed : BoardWallpaperMode.Normal;
        }));
        wallpaperButtons.Children.Add(ActionButton("Choose folder to rotate", "BoardFolderButton", () =>
        {
            using var folder = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose your wallpaper collection", UseDescriptionForTitle = true };
            if (folder.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            _boardWallpaper.Text = folder.SelectedPath;
            _wallpaperMode.SelectedValue = SelectedWallpaperMode is BoardWallpaperMode.Timed or BoardWallpaperMode.RotatingTimed ? BoardWallpaperMode.RotatingTimed : BoardWallpaperMode.Rotating;
        }));
        moreOptions.Children.Add(ActionButton("Use current wallpaper", "CaptureBoardButton", () =>
        {
            _boardWallpaper.Text = DesktopCanvasFeatures.CaptureWallpaper(_store, _activeBoard ?? Guid.Empty);
            _wallpaperMode.SelectedValue = SelectedWallpaperMode is BoardWallpaperMode.Timed or BoardWallpaperMode.RotatingTimed ? BoardWallpaperMode.Timed : BoardWallpaperMode.Normal;
        }));
        boardOptions.Children.Add(wallpaperButtons);
        Field(_rotationFields, "Change wallpaper every (minutes)", _rotationMinutes); _rotationFields.Children.Add(_rotationRandom);
        moreOptions.Children.Add(_rotationFields);
        Field(_overrideFields, "Wallpaper for the timed period", _overrideWallpaper);
        _overrideFields.Children.Add(ActionButton("Choose timed wallpaper", "TimedWallpaperButton", () => ChooseWallpaper(_overrideWallpaper)));
        Field(_overrideFields, "Start time (HH:mm, local)", _overrideStart); Field(_overrideFields, "End time (HH:mm, local)", _overrideEnd);
        _overrideFields.Children.Add(new TextBlock { Text = "Repeats daily. Overnight periods are supported. Your normal wallpaper or rotation returns when the period ends.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        moreOptions.Children.Add(_overrideFields);
        Field(moreOptions, "Daily board switch time (HH:mm, blank for manual only)", _boardTime);
        _autoBoards.Margin = new Thickness(0, 10, 0, 10); moreOptions.Children.Add(_autoBoards);
        var optionsHeader = new TextBlock { Text = "More options" }; optionsHeader.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var optionsExpander = new Expander { Header = optionsHeader, Content = moreOptions, Margin = new Thickness(0, 12, 0, 12) };
        optionsExpander.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush"); RegisterName("BoardMoreOptions", optionsExpander);
        boardOptions.Children.Add(optionsExpander);
        boardOptions.Children.Add(ActionButton("Apply wallpaper", "SaveBoardButton", SaveBoard));
        boardPanel.Children.Add(boardOptions);
        root.Children.Add(boardPanel);
        root.Children.Add(_widgetsStep);
        _widgetsStep.Children.Add(new TextBlock { Text = "Widgets", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var hostButtons = new WrapPanel();
        hostButtons.Children.Add(ActionButton("Show widgets", "ShowWidgetsButton", ShowWidgets));
        hostButtons.Children.Add(ActionButton("Hide widgets", "HideWidgetsButton", HideWidgets));
        hostButtons.Children.Add(ActionButton("Find widgets", "FindWidgetsButton", FindWidgets));
        _widgetsStep.Children.Add(hostButtons);
        _widgetsStep.Children.Add(_status);
        _status.Margin = new Thickness(0, 6, 0, 16);
        var addButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 20) };
        foreach (var kind in Enum.GetValues<DesktopWidgetKind>())
        {
            var captured = kind;
            addButtons.Children.Add(ActionButton("+ " + kind, "Add" + kind + "Button", () => Add(captured)));
        }
        _widgetsStep.Children.Add(addButtons);
        _widgetsStep.Children.Add(new TextBlock { Text = "Or drop an image, link, or note here.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 16) });
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var listPanel = new StackPanel();
        listPanel.Children.Add(new TextBlock { Text = "YOUR WIDGETS", FontSize = 10, Margin = new Thickness(0, 0, 0, 10) });
        listPanel.Children.Add(_list);
        listPanel.Children.Add(new TextBlock { Text = "Drag a widget’s title bar, or its body when the title is hidden, to move it. Drag its lower corner to resize.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 14, 0, 0) });
        columns.Children.Add(listPanel);
        var editor = new StackPanel();
        Grid.SetColumn(editor, 2); columns.Children.Add(editor);
        editor.Children.Add(_empty); editor.Children.Add(_fields);
        _fields.Children.Add(_kindLabel);
        Field(_fields, "Title (optional)", _title);
        _contentLabel.Margin = new Thickness(0, 12, 0, 6);
        _fields.Children.Add(_contentLabel); _fields.Children.Add(_content); _fields.Children.Add(_browse);
        Field(_linkFields, "Custom description (optional)", _linkDescription);
        Field(_linkFields, "LeetCode username (LeetCode links only)", _leetCodeUser);
        _linkFields.Children.Add(ActionButton("Refresh link details", "RefreshLinkButton", RefreshLink));
        _fields.Children.Add(_linkFields);
        Field(_targetFields, "Target date", _date);
        Field(_targetFields, "Local time (HH:mm)", _time);
        _fields.Children.Add(_targetFields);
        Field(_fields, "Width", _width); Field(_fields, "Height", _height);
        var presets = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        foreach (var name in new[] { "Glass", "Paper", "Minimal", "Gothic", "Cathedral", "Crimson", "Parchment", "Royal", "Neon", "Terminal" })
        {
            var preset = name;
            var button = ActionButton(preset, "Style" + preset + "Button", () => ApplyStyle(preset));
            button.Margin = new Thickness(0, 0, 8, 8);
            presets.Children.Add(button);
        }
        _fields.Children.Add(new TextBlock { Text = "Customize", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 0) });
        _fields.Children.Add(presets);
        _fields.Children.Add(_glassEffect);
        Field(_fields, "Font (installed fonts, or type a name)", _font);
        Field(_fields, "Text size", _fontSize);
        Field(_fields, "Text alignment", _alignment);
        _fields.Children.Add(_bold);
        _fields.Children.Add(_italic);
        Field(_fields, "Image fit", _imageFit);
        ColorField("Text color", _textColor);
        ColorField("Background color", _backgroundColor);
        ColorField("Border color", _borderColor);
        Field(_fields, "Border thickness", _borderWidth);
        Field(_fields, "Opacity (%)", _opacity);
        Field(_fields, "Rounded corners", _radius);
        foreach (var option in new[] { _showHeader, _showBorder, _showBackground })
        { option.Margin = new Thickness(0, 10, 0, 0); _fields.Children.Add(option); }
        _fields.Children.Add(_enabled); _fields.Children.Add(_locked);
        var editorButtons = new WrapPanel { Margin = new Thickness(16, 12, 16, 12) };
        _save = ActionButton("Create widget", "SaveWidgetButton", Save);
        _save.SetResourceReference(StyleProperty, "PrimaryButton");
        editorButtons.Children.Add(_save);
        _duplicate.Click += (_, _) => Run(Duplicate);
        editorButtons.Children.Add(_duplicate); RegisterName("DuplicateWidgetButton", _duplicate);
        _delete.Click += (_, _) => Run(Delete);
        editorButtons.Children.Add(_delete); RegisterName("DeleteWidgetButton", _delete);
        root.Children.Add(columns);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workflowScroll.Content = root; layout.Children.Add(_workflowScroll);
        var footer = new StackPanel();
        _message.Margin = new Thickness(16, 10, 16, 0); footer.Children.Add(_message); footer.Children.Add(editorButtons);
        var footerLayout = new Grid();
        footerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        footerLayout.ColumnDefinitions.Add(new ColumnDefinition());
        var previewPanel = new StackPanel { Margin = new Thickness(16, 12, 0, 12) };
        previewPanel.Children.Add(new TextBlock { Text = "LIVE PREVIEW", FontSize = 10 });
        previewPanel.Children.Add(_preview);
        footerLayout.Children.Add(previewPanel);
        Grid.SetColumn(footer, 1); footer.VerticalAlignment = VerticalAlignment.Center; footerLayout.Children.Add(footer);
        var footerBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = footerLayout };
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        footerBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        Grid.SetRow(footerBorder, 1); layout.Children.Add(footerBorder);
        Content = layout;
        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true; };
        PreviewDrop += (_, e) =>
        {
            Run(() => CreateFromDrop(e.Data));
            e.Handled = true;
        };
        _boards.SelectionChanged += (_, _) =>
        {
            if (_loadingBoards || _boards.SelectedItem is not DesktopBoard board) return;
            Run(() => { _store.Update(document => { document.SwitchBoard(board.Id, DateTime.Now); document.Enabled = true; }); ClearEditor(); RefreshList(); _startHost(); });
        };
        RegisterName("BoardList", _boards); RegisterName("BoardName", _boardName); RegisterName("BoardWallpaper", _boardWallpaper);
        RegisterName("BoardTime", _boardTime); RegisterName("AutoBoards", _autoBoards);
        RegisterName("WallpaperMode", _wallpaperMode); RegisterName("RotationMinutes", _rotationMinutes); RegisterName("RotationRandom", _rotationRandom);
        RegisterName("TimedWallpaper", _overrideWallpaper); RegisterName("WallpaperStart", _overrideStart); RegisterName("WallpaperEnd", _overrideEnd);
        RegisterName("LinkDescription", _linkDescription); RegisterName("LeetCodeUsername", _leetCodeUser);
        RegisterName("WidgetList", _list); RegisterName("WidgetTitle", _title); RegisterName("WidgetContent", _content);
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
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is DesktopWidget widget) Select(widget, false); };
        _browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose a canvas image", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) _content.Text = dialog.FileName;
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshStatus();
        IsVisibleChanged += (_, _) => _timer.IsEnabled = IsVisible;
        Unloaded += (_, _) => _timer.Stop();
        Loaded += (_, _) => { Run(RefreshList); RefreshStatus(); _timer.Start(); };
        _fields.Visibility = Visibility.Collapsed;
        _save.IsEnabled = false; _duplicate.IsEnabled = false; _delete.IsEnabled = false;
        foreach (var box in new[] { _title, _content, _textColor, _backgroundColor, _borderColor, _time, _linkDescription, _leetCodeUser }) box.TextChanged += (_, _) => RefreshPreview();
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
        Run(RefreshList);
    }

    private Button ActionButton(string text, string name, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 8, 10, 8) };
        button.Click += (_, _) => Run(action);
        RegisterName(name, button);
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        return button;
    }
    internal void CreateFromDrop(System.Windows.IDataObject data)
    {
        var widgets = DesktopCanvasFeatures.ReadDrop(data);
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        DesktopCanvasFeatures.SaveDrop(_store, widgets, area.Right - 340, area.Top + 70);
        _selected = widgets[0]; RefreshList(); _startHost(); _message.Text = $"Created {widgets.Count} widget(s) on this board.";
    }
    private static void Field(StackPanel panel, string label, FrameworkElement control)
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 6), FontSize = 12 };
        if (control is Slider slider)
        { caption.Text = $"{label} · {slider.Value:0.#}"; slider.ValueChanged += (_, _) => caption.Text = $"{label} · {slider.Value:0.#}"; }
        panel.Children.Add(caption);
        panel.Children.Add(control);
    }
    private sealed record ColorOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;
    private void ColorField(string label, TextBox input)
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
        Field(_fields, label + " (#RRGGBB)", row);
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
        _previewWindow.Update(draft);
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
            if (_activeBoard != document.ActiveBoardId)
            {
                RefreshList();
                if (_selected != null) _message.Text = "The board switched. Your draft is still here; saving updates its original board.";
                return;
            }
            _status.Text = document.Widgets.Count == 0 ? "No widgets saved yet. Add one, then choose Create widget below."
                : DesktopCanvasProcess.IsRunning(_store.FilePath) ? $"{document.Widgets.Count(w => w.Enabled)} widgets running independently."
                : "Desktop widgets are hidden. Choose Show widgets. Press Win+D to view your desktop.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void RefreshList()
    {
        var selected = _selected?.Id;
        var document = _store.Load();
        RefreshBoards(document);
        _list.ItemsSource = document.Widgets;
        _list.SelectedItem = document.Widgets.FirstOrDefault(w => w.Id == selected);
        RefreshStatus();
    }
    private void Add(DesktopWidgetKind kind)
    {
        var document = _store.Load();
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var offset = document.Widgets.Count % 8 * 28;
        Select(new DesktopWidget { Kind = kind, Title = kind == DesktopWidgetKind.Note ? "A little reminder" : "New " + kind.ToString().ToLowerInvariant(),
            Content = kind == DesktopWidgetKind.Note ? "Make room for what matters." : "", FontSize = kind == DesktopWidgetKind.Countdown ? 30 : 16,
            X = area.Right - 340 - offset, Y = area.Top + 70 + offset }, true);
        if (kind == DesktopWidgetKind.Link) _leetCodeUser.Text = "_Unkillable__Demon_King";
        _list.SelectedItem = null;
        _message.Text = "Save to place this widget on your desktop.";
    }
    private void Select(DesktopWidget widget, bool isNew)
    {
        _loading = true;
        _selected = widget; _isNew = isNew;
        _editingBoard = _activeBoard;
        _glassEffect.IsChecked = widget.GlassEffect;
        _fields.Visibility = Visibility.Visible; _empty.Visibility = Visibility.Collapsed;
        _kindLabel.Text = (isNew ? "New " : "Edit ") + widget.Kind.ToString().ToLowerInvariant();
        _title.Text = widget.Title; _content.Text = widget.Content;
        _linkDescription.Text = widget.LinkCustomDescription; _leetCodeUser.Text = widget.LeetCodeUsername;
        _linkFields.Visibility = widget.Kind == DesktopWidgetKind.Link ? Visibility.Visible : Visibility.Collapsed;
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
        _content.Visibility = widget.Kind is DesktopWidgetKind.Countdown or DesktopWidgetKind.Sketch ? Visibility.Collapsed : Visibility.Visible;
        _contentLabel.Visibility = _content.Visibility;
        _contentLabel.Text = widget.Kind switch { DesktopWidgetKind.Image => "Image file", DesktopWidgetKind.Link => "Website address (https://…)", _ => "Your note" };
        _browse.Visibility = widget.Kind == DesktopWidgetKind.Image ? Visibility.Visible : Visibility.Collapsed;
        _content.AcceptsReturn = widget.Kind == DesktopWidgetKind.Note;
        _content.MinHeight = widget.Kind == DesktopWidgetKind.Note ? 110 : 36;
        _delete.IsEnabled = !isNew;
        _duplicate.IsEnabled = !isNew; _save.IsEnabled = true;
        _save.Content = isNew ? "Create widget" : "Save changes";
        _imageFit.Visibility = widget.Kind == DesktopWidgetKind.Image ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
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
            var board = document.Boards.FirstOrDefault(item => item.Id == _editingBoard) ?? throw new InvalidOperationException("The original board was removed. Copy your draft into a new widget.");
            var saved = board.Widgets.FirstOrDefault(w => w.Id == _selected.Id);
            if (saved == null && !_isNew) throw new InvalidOperationException("This widget was removed. Add another widget.");
            if (saved == null) { saved = _selected; board.Widgets.Add(saved); document.Enabled = true; }
            if (saved.Content != content || saved.LeetCodeUsername != _leetCodeUser.Text.Trim())
            {
                saved.LinkUpdatedAt = null; saved.LinkPageTitle = ""; saved.LinkDescription = ""; saved.LinkStatus = "";
                saved.PotdCompletions.Clear(); saved.ActivityStreak = 0; saved.PotdDate = null; saved.PotdTitle = ""; saved.PotdUrl = "";
            }
            saved.Title = title; saved.Content = content; saved.Target = target;
            saved.LinkCustomDescription = _linkDescription.Text.Trim(); saved.LeetCodeUsername = _leetCodeUser.Text.Trim();
            if (_width.Value != _loadedWidth) saved.Width = _width.Value;
            if (_height.Value != _loadedHeight) saved.Height = _height.Value;
            saved.Enabled = _enabled.IsChecked == true; saved.Locked = _locked.IsChecked == true;
            ReadAppearance(saved);
            if (saved.Enabled) document.Enabled = true;
        });
        _isNew = false;
        RefreshList();
        _startHost();
        _message.Text = _editingBoard != _activeBoard ? "Saved to the original board. Switch back to see this widget." : _enabled.IsChecked == true ? "Saved to your desktop. Press Win+D to see your widgets."
            : "Saved as hidden. Enable Show this widget when you’re ready.";
    }
    private void Delete()
    {
        if (_selected == null || _isNew) return;
        _store.Update(document => { foreach (var board in document.Boards) board.Widgets.RemoveAll(w => w.Id == _selected.Id); });
        _selected = null;
        _save.IsEnabled = false; _duplicate.IsEnabled = false; _delete.IsEnabled = false;
        _preview.Child = null; _previewWindow?.Close(); _previewWindow = null;
        _fields.Visibility = Visibility.Collapsed; _empty.Visibility = Visibility.Visible;
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
        _message.Text = "Widgets moved into view on your desktop. Press Win+D to see them.";
    }
    private void Duplicate()
    {
        if (_selected == null || _isNew) return;
        if (_editingBoard != _activeBoard) throw new InvalidOperationException("Switch back to the original board to duplicate this widget.");
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
        _status.Text = "Desktop widgets hidden. Your canvas is saved.";
    }

    private void RefreshBoards(DesktopCanvasDocument document)
    {
        _loadingBoards = true;
        _activeBoard = document.ActiveBoardId;
        _boards.ItemsSource = document.Boards; _boards.SelectedItem = document.ActiveBoard;
        _boardName.Text = document.ActiveBoard!.Name; _boardWallpaper.Text = document.ActiveBoard.WallpaperPath;
        _boardScene = document.ActiveBoard.Scene; _boardProfile.SelectedItem = null; _boardFit.SelectedItem = document.ActiveBoard.WallpaperFit;
        _wallpaperMode.SelectedValue = document.ActiveBoard.EffectiveWallpaperMode;
        _rotationMinutes.Text = (document.ActiveBoard.RotationMinutes ?? (document.ActiveBoard.Scene?.SlideshowIntervalMinutes > 0 ? document.ActiveBoard.Scene.SlideshowIntervalMinutes : 15)).ToString(CultureInfo.InvariantCulture);
        _rotationRandom.IsChecked = document.ActiveBoard.RotationRandom ?? document.ActiveBoard.Scene?.SlideshowRandom ?? false;
        _overrideWallpaper.Text = document.ActiveBoard.OverrideWallpaperPath;
        _overrideStart.Text = document.ActiveBoard.OverrideStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        _overrideEnd.Text = document.ActiveBoard.OverrideEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        _boardTime.Text = document.ActiveBoard.SwitchAt?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        _autoBoards.IsChecked = document.AutoSwitchBoards;
        _loadingBoards = false;
        RefreshWallpaperFields();
    }
    private BoardWallpaperMode SelectedWallpaperMode => _wallpaperMode.SelectedValue is BoardWallpaperMode mode ? mode : BoardWallpaperMode.Normal;
    private void RefreshWallpaperFields()
    {
        var rotating = SelectedWallpaperMode is BoardWallpaperMode.Rotating or BoardWallpaperMode.RotatingTimed;
        var timed = SelectedWallpaperMode is BoardWallpaperMode.Timed or BoardWallpaperMode.RotatingTimed;
        _rotationFields.Visibility = rotating ? Visibility.Visible : Visibility.Collapsed;
        _overrideFields.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
        _wallpaperSourceLabel.Text = rotating ? "Wallpaper collection folder" : timed ? "Normal wallpaper (outside the timed period)" : "Wallpaper image or video (optional)";
        _wallpaperHint.Text = SelectedWallpaperMode switch
        {
            BoardWallpaperMode.Normal => "Keep one wallpaper on this board. Leave it empty to set up your widgets first.",
            BoardWallpaperMode.Timed => "Use your normal wallpaper, then switch to another wallpaper during a daily time window.",
            BoardWallpaperMode.Rotating => "Rotate through a folder of wallpapers at your chosen interval.",
            _ => "Rotate your wallpapers, pause for another wallpaper during a daily time window, then return to rotation."
        };
    }
    private void ChooseWallpaper(TextBox target)
    {
        var dialog = new OpenFileDialog { Title = "Choose wallpaper", Filter = "Wallpaper media|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.webm" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) target.Text = dialog.FileName;
    }
    private void ClearEditor()
    {
        _selected = null; _isNew = false; _list.SelectedItem = null;
        _save.IsEnabled = false; _delete.IsEnabled = false; _duplicate.IsEnabled = false;
        _preview.Child = null; _previewWindow?.Close(); _previewWindow = null;
        _fields.Visibility = Visibility.Collapsed; _empty.Visibility = Visibility.Visible;
    }
    private void CreateBoard(bool copy)
    {
        _store.Update(document =>
        {
            var board = copy ? DesktopCanvasFeatures.CopyBoard(document.ActiveBoard!, document.ActiveBoard!.Name + " copy") : new DesktopBoard();
            document.Boards.Add(board); document.SwitchBoard(board.Id, DateTime.Now); document.Enabled = true;
        });
        ClearEditor(); RefreshList(); _startHost();
        if (!copy) { _workflowScroll.ScrollToTop(); _message.Text = "New board, clean slate. Choose your wallpaper mode, then add your widgets."; }
    }
    private void CreateDarkBoard()
    {
        _store.Update(document =>
        {
            if (document.Boards.Any(board => board.SwitchAt == TimeOnly.MinValue)) throw new InvalidOperationException("A midnight board already exists. Edit its settings or choose a different time.");
            var source = document.ActiveBoard!;
            if (source.WallpaperPath.Length == 0)
            {
                var sourceProfile = _boardProfile.Items.Cast<WallpaperProfiles.Models.WallpaperProfile>().FirstOrDefault(profile => profile.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                if (sourceProfile != null) { source.Scene = DesktopCanvasFeatures.CopyScene(sourceProfile); source.WallpaperPath = sourceProfile.FolderPath; source.WallpaperFit = sourceProfile.FitMode; }
                else
                {
                    try { source.WallpaperPath = DesktopCanvasFeatures.CaptureWallpaper(_store, source.Id); }
                    catch (InvalidOperationException) { }
                }
            }
            var dark = DesktopCanvasFeatures.CopyBoard(source, "After midnight"); dark.SwitchAt = TimeOnly.MinValue;
            var darkProfile = _boardProfile.Items.Cast<WallpaperProfiles.Models.WallpaperProfile>().FirstOrDefault(profile => profile.Name.Equals("Dark", StringComparison.OrdinalIgnoreCase));
            if (darkProfile != null)
            {
                dark.Scene = DesktopCanvasFeatures.CopyScene(darkProfile); dark.WallpaperPath = darkProfile.FolderPath; dark.WallpaperFit = darkProfile.FitMode;
                dark.WallpaperMode = null; dark.RotationMinutes = null; dark.RotationRandom = null; dark.OverrideWallpaperPath = "";
            }
            foreach (var widget in dark.Widgets)
            { widget.BackgroundColor = "#111318"; widget.TextColor = "#DFE4EE"; widget.BorderColor = "#414754"; }
            source.SwitchAt ??= new TimeOnly(8, 0);
            document.Boards.Add(dark); document.AutoSwitchBoards = true; document.Enabled = true;
            document.ManualBoardUntil = null; document.ApplyBoardSchedule(DateTime.Now);
        });
        ClearEditor(); RefreshList(); _startHost();
        _message.Text = "Midnight board added. Choose its wallpaper in Board settings; your daytime board returns at its saved time.";
    }
    private void DeleteBoard()
    {
        _store.Update(document =>
        {
            if (document.Boards.Count == 1) throw new InvalidOperationException("Keep at least one board.");
            document.Boards.RemoveAll(board => board.Id == _activeBoard);
            document.SwitchBoard(document.Boards[0].Id, DateTime.Now);
        });
        ClearEditor(); RefreshList(); _startHost();
    }
    private void SaveBoard()
    {
        if (string.IsNullOrWhiteSpace(_boardName.Text)) throw new InvalidOperationException("Give this board a name.");
        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(_boardTime.Text))
        {
            if (!TimeOnly.TryParseExact(_boardTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new InvalidOperationException("Enter a board time as HH:mm, or leave it blank.");
            time = parsed;
        }
        var wallpaper = _boardWallpaper.Text.Trim();
        var mode = SelectedWallpaperMode;
        var rotating = mode is BoardWallpaperMode.Rotating or BoardWallpaperMode.RotatingTimed;
        var timed = mode is BoardWallpaperMode.Timed or BoardWallpaperMode.RotatingTimed;
        var rotationMinutes = 15;
        if (rotating && (!int.TryParse(_rotationMinutes.Text.Trim(), out rotationMinutes) || rotationMinutes < 1 || rotationMinutes > 1440))
            throw new InvalidOperationException("Choose a rotation interval from 1 to 1440 minutes.");
        if (rotating && !Directory.Exists(wallpaper)) throw new InvalidOperationException("Choose a wallpaper collection folder for rotation.");
        if (!rotating && Directory.Exists(wallpaper) && _boardScene?.FolderPath != wallpaper) throw new InvalidOperationException("Choose one wallpaper file, or select a rotating wallpaper mode for a folder.");
        var overrideWallpaper = _overrideWallpaper.Text.Trim();
        var overrideStart = new TimeOnly(20, 0); var overrideEnd = new TimeOnly(8, 0);
        if (timed)
        {
            if (string.IsNullOrWhiteSpace(wallpaper)) throw new InvalidOperationException("Choose the wallpaper to return to after the timed period.");
            if (!File.Exists(overrideWallpaper) || (!WallpaperEngine.IsImageFile(overrideWallpaper) && !WallpaperEngine.IsVideoFile(overrideWallpaper)))
                throw new InvalidOperationException("Choose an existing wallpaper for the timed period.");
            if (!TimeOnly.TryParseExact(_overrideStart.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out overrideStart)
                || !TimeOnly.TryParseExact(_overrideEnd.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out overrideEnd) || overrideStart == overrideEnd)
                throw new InvalidOperationException("Enter different start and end times as HH:mm.");
        }
        if (wallpaper.Length > 0 && !Directory.Exists(wallpaper) && (!File.Exists(wallpaper) || (!WallpaperEngine.IsImageFile(wallpaper) && !WallpaperEngine.IsVideoFile(wallpaper))))
            throw new InvalidOperationException("Choose an existing wallpaper image, video, or profile folder, or leave it blank.");
        _store.Update(document =>
        {
            var board = document.Boards.FirstOrDefault(item => item.Id == _activeBoard) ?? throw new InvalidOperationException("This board was removed.");
            if (time.HasValue && document.Boards.Any(item => item.Id != board.Id && item.SwitchAt == time))
                throw new InvalidOperationException("Each board needs a different switch time.");
            board.Name = _boardName.Text.Trim(); board.WallpaperPath = wallpaper; board.SwitchAt = time;
            board.WallpaperFit = _boardFit.SelectedItem is WallpaperProfiles.Models.FitMode fit ? fit : WallpaperProfiles.Models.FitMode.Fill;
            board.Scene = _boardScene?.FolderPath == wallpaper ? _boardScene : null;
            board.WallpaperMode = mode;
            board.RotationMinutes = rotating ? rotationMinutes : null; board.RotationRandom = rotating ? _rotationRandom.IsChecked == true : null;
            board.OverrideWallpaperPath = timed ? overrideWallpaper : ""; board.OverrideStart = overrideStart; board.OverrideEnd = overrideEnd;
            document.AutoSwitchBoards = _autoBoards.IsChecked == true;
            document.ManualBoardUntil = null; document.Enabled = true;
        });
        RefreshList(); _startHost(); _message.Text = "Board settings saved.";
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
