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
    private readonly StackPanel _workspaceActions = new();
    private Grid _workspaceBody = null!;
    private bool _compactWorkspace;
    private (bool Compact, bool Editing)? _workspaceLayout;

    private FrameworkElement BuildWorkspace()
    {
        var workspace = new Grid { Margin = new Thickness(16) };
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition());
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headerFrame = new Border { Style = (Style)FindResource("WorkspaceBanner"), Margin = new Thickness(0, 0, 0, 16) };
        var header = new Grid(); headerFrame.Child = header;
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Widgets", FontSize = 20, FontWeight = FontWeights.SemiBold });

        var quickAdd = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var kind in new[] { DesktopWidgetKind.Clock, DesktopWidgetKind.Visualizer, DesktopWidgetKind.NowPlaying })
        {
            var captured = kind;
            var quick = ActionButton("+ " + (kind == DesktopWidgetKind.NowPlaying ? "Now playing" : kind.ToString()), "QuickAdd" + kind + "Button", () => Add(captured));
            quick.Padding = new Thickness(12, 9, 12, 9); quick.FontSize = 12;
            quick.SetResourceReference(Control.BackgroundProperty, "SurfaceRaisedBrush");
            var glyph = WidgetGlyph(kind);
            var quickContent = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var icon = new TextBlock { Text = glyph, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            quickContent.Children.Add(icon);
            quickContent.Children.Add(new TextBlock { Text = kind == DesktopWidgetKind.NowPlaying ? "Now playing" : kind.ToString(), VerticalAlignment = VerticalAlignment.Center });
            quick.Content = quickContent;
            quickAdd.Children.Add(quick);
        }
        header.Children.Add(heading);
        var add = ActionButton("+  Add widget", "AddWidgetButton", () => _addPopup.IsOpen = !_addPopup.IsOpen);
        add.SetResourceReference(StyleProperty, "PrimaryButton"); add.VerticalAlignment = VerticalAlignment.Center; add.Margin = new Thickness(0);
        var headerActions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var more = new Button { Content = "\uE712", Margin = new Thickness(0, 0, 8, 0) };
        RegisterName("WidgetActionsButton", more);
        more.FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"); more.Width = 36; more.Padding = new Thickness(0);
        more.ToolTip = "Widget actions";
        System.Windows.Automation.AutomationProperties.SetName(more, "Widget actions");
        var menu = new System.Windows.Controls.Primitives.Popup { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true };
        var menuFrame = new Border { Child = _workspaceActions, Width = 288, Padding = new Thickness(8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        menuFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush"); menuFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        menu.Child = menuFrame; workspace.Children.Add(menu);
        more.Click += (_, _) => menu.IsOpen = !menu.IsOpen;
        menu.Opened += (_, _) => _workspaceActions.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        _workspaceActions.PreviewKeyDown += (_, e) => { if (e.Key != System.Windows.Input.Key.Escape) return; menu.IsOpen = false; more.Focus(); e.Handled = true; };
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(_workspaceActions, System.Windows.Input.KeyboardNavigationMode.Cycle);
        _workspaceActions.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, _) => menu.IsOpen = false));
        _workspaceActions.Children.Add(ActionButton("Styles", "OpenStyleCatalogueButton", () => OpenCatalogue(false)));
        _workspaceActions.Children.Add(new TextBlock { Text = "Quick add", FontSize = 12, Margin = new Thickness(8) });
        quickAdd.Margin = new Thickness(0, 0, 0, 8); _workspaceActions.Children.Add(quickAdd);
        headerActions.Children.Add(more); headerActions.Children.Add(add);
        Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions); workspace.Children.Add(headerFrame);
        _addPopup.PlacementTarget = add; _addPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        var picker = new StackPanel();
        foreach (var kind in Enum.GetValues<DesktopWidgetKind>())
        {
            var captured = kind;
            var label = kind == DesktopWidgetKind.NowPlaying ? "Now playing" : kind.ToString();
            var choice = ActionButton(label, "Add" + kind + "Button", () => { _addPopup.IsOpen = false; Add(captured); });
            var choiceContent = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            choiceContent.Children.Add(new TextBlock { Text = WidgetGlyph(kind), FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            choiceContent.Children.Add(new TextBlock { Text = label, FontSize = 14 });
            choice.Content = choiceContent; choice.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            choice.Margin = new Thickness(0, 2, 0, 2); choice.BorderThickness = new Thickness(0); choice.MinWidth = 170; picker.Children.Add(choice);
        }
        var pickerFrame = new Border { Child = picker, CornerRadius = new CornerRadius(12), Padding = new Thickness(8), BorderThickness = new Thickness(1) };
        pickerFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush"); pickerFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _addPopup.Child = pickerFrame; workspace.Children.Add(_addPopup); RegisterName("WidgetTypePicker", _addPopup);

        var body = _workspaceBody = new Grid();
        body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(body, 2); workspace.Children.Add(body);
        body.ColumnDefinitions.Add(_galleryColumn); body.ColumnDefinitions.Add(_editorColumn);
        var gallery = _galleryFrame; gallery.Margin = new Thickness(0, 0, 16, 0); body.Children.Add(gallery);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var actions = new WrapPanel { MaxWidth = 270 };
        _showAll = ActionButton("Show widgets", "ShowWidgetsButton", ShowWidgets); _hideAll = ActionButton("Hide widgets", "HideWidgetsButton", HideWidgets);
        actions.Children.Add(_showAll); actions.Children.Add(_hideAll);
        var find = ActionButton("Find widgets", "FindWidgetsButton", FindWidgets); find.ToolTip = "Bring widgets into view"; find.FontSize = 12; find.Padding = new Thickness(8, 2, 8, 2); actions.Children.Add(find);
        foreach (Button action in actions.Children.Cast<Button>().ToArray()) { actions.Children.Remove(action); _workspaceActions.Children.Add(action); }
        DockPanel.SetDock(_scopeFilter, Dock.Left); toolbar.Children.Add(_scopeFilter);
        _status.Margin = new Thickness(0); _status.FontSize = 12; _status.VerticalAlignment = VerticalAlignment.Center;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); toolbar.Children.Add(_status);
        var studioToolbar = new WrapPanel();
        toolbar.Margin = new Thickness(0, 0, 16, 8);
        studioToolbar.Children.Add(toolbar); studioToolbar.Children.Add(BuildCanvasToolbar());
        Grid.SetRow(studioToolbar, 1); workspace.Children.Add(studioToolbar);
        var galleryArea = new Grid(); gallery.Children.Add(galleryArea);
        _list.Background = System.Windows.Media.Brushes.Transparent; _list.BorderThickness = new Thickness(0); _list.Padding = new Thickness(0);
        _list.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        _list.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        _list.ItemContainerStyle = GalleryItemStyle(); _list.DisplayMemberPath = ""; _list.ItemTemplate = GalleryCardTemplate(); galleryArea.Children.Add(_list);
        _empty.Text = "Drop a file, link or note"; _empty.FontSize = 14; _empty.TextAlignment = TextAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center; _empty.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); _empty.IsHitTestVisible = false; galleryArea.Children.Add(_empty);

        _overviewFrame.CornerRadius = new CornerRadius(0); _overviewFrame.Padding = new Thickness(0);
        _overviewFrame.BorderThickness = new Thickness(0);
        _overviewFrame.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        _overviewFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var overview = new Grid(); _overviewFrame.Child = overview;
        var desktopPreview = new Viewbox { Child = _desktopOverview, Stretch = Stretch.Uniform };
        overview.Children.Add(desktopPreview);
        _overviewCaption.FontSize = 12; _overviewCaption.TextTrimming = TextTrimming.CharacterEllipsis;
        _overviewCaption.Foreground = System.Windows.Media.Brushes.White;
        var overviewHeading = new StackPanel();
        overviewHeading.Children.Add(new TextBlock { Text = "Desktop preview", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White });
        overviewHeading.Children.Add(_overviewCaption);
        var previewLabel = new Border { Child = overviewHeading, Style = (Style)FindResource("PreviewOverlay"), VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(8), Padding = new Thickness(12, 8, 12, 8), IsHitTestVisible = false };
        overview.Children.Add(previewLabel);
        var overviewHint = new TextBlock { Text = "Drag to arrange · Save to keep", FontSize = 12 };
        overviewHint.Foreground = System.Windows.Media.Brushes.White;
        overview.Children.Add(new Border { Child = overviewHint, Style = (Style)FindResource("PreviewOverlay"), VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(8), IsHitTestVisible = false });
        overview.ToolTip = "Drag widgets to arrange them. Save to keep your composition. Each wallpaper restores its saved layout when it plays.";
        Grid.SetColumn(_overviewFrame, 1); body.Children.Add(_overviewFrame);

        _editorFrame.CornerRadius = new CornerRadius(16); _editorFrame.Padding = new Thickness(16); _editorFrame.BorderThickness = new Thickness(1);
        _editorFrame.Background = System.Windows.Media.Brushes.Transparent; _editorFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _editorFrame.Margin = new Thickness(0, 0, 14, 0); body.Children.Add(_editorFrame);
        var editor = new Grid(); _editorFrame.Child = editor;
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) editor.RowDefinitions.Add(new RowDefinition { Height = height });
        var editorHeader = new DockPanel(); _kindLabel.Margin = new Thickness(0); _kindLabel.VerticalAlignment = VerticalAlignment.Center;
        var close = ActionButton("Close", "CloseWidgetEditorButton", CloseEditor); close.ToolTip = "Close editor"; close.Padding = new Thickness(8, 2, 8, 2); close.Margin = new Thickness(0); close.FontSize = 12; close.BorderThickness = new Thickness(0);
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
        _musicFields.Children.Add(new TextBlock { Text = "Song details and controls use music players such as Spotify, Apple Music, and TIDAL. The audio spectrum reacts to all desktop audio. Drag the song text or spectrum to move the widget.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 12 });
        Field(_musicFields, "Player layout", _musicLayout);
        foreach (var option in new[] { _albumArt, _playbackControls, _playbackProgress, _audioVisualizer }) { option.Margin = new Thickness(0, 10, 0, 0); _musicFields.Children.Add(option); }
        ColorField("Playback accent", _musicAccent, _musicFields);
        _contentPage.Children.Add(_musicFields);
        _clockFields.Children.Add(new TextBlock { Text = "Choose a clock design in Style. Pair it with any visualizer, or add a visualizer as a separate widget.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 12 });
        foreach (var (name, option) in new[] { ("Clock24Hour", _clock24Hour), ("ClockGreeting", _clockGreeting), ("ClockVisualizer", _clockVisualizer) })
        {
            option.Margin = new Thickness(0, 10, 0, 0); _clockFields.Children.Add(option); RegisterName(name, option);
            System.Windows.Automation.AutomationProperties.SetName(option, option.Content.ToString());
        }
        _contentPage.Children.Add(_clockFields);
        BuildSkinControls();
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
        Field(_layoutPage, "Position X", _positionX); Field(_layoutPage, "Position Y", _positionY);
        Field(_layoutPage, "Width", _width); Field(_layoutPage, "Height", _height); Field(_layoutPage, "Image fit", _imageFit);
        _layoutPage.Children.Add(_enabled); _layoutPage.Children.Add(_locked);
        _editorScroll.Content = _fields; Grid.SetRow(_editorScroll, 3); editor.Children.Add(_editorScroll); RegisterName("WidgetEditorScroll", _editorScroll);
        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _save = ActionButton("Create", "SaveWidgetButton", Save);  footer.Children.Add(_save);
        _duplicate.Content = "Duplicate"; _duplicate.ToolTip = "Duplicate widget"; _duplicate.Padding = new Thickness(10, 6, 10, 6); _duplicate.Margin = new Thickness(0, 0, 6, 0);
        _duplicate.Click += (_, _) => Run(Duplicate); Grid.SetColumn(_duplicate, 1); footer.Children.Add(_duplicate); RegisterName("DuplicateWidgetButton", _duplicate);
        _delete.Content = "Delete"; _delete.SetResourceReference(StyleProperty, "DangerButton"); _delete.ToolTip = "Delete widget"; _delete.Padding = new Thickness(10, 6, 10, 6); _delete.Margin = new Thickness(0);
        _delete.Click += (_, _) => Run(Delete); Grid.SetColumn(_delete, 2); footer.Children.Add(_delete); RegisterName("DeleteWidgetButton", _delete);
        Grid.SetRow(footer, 4); editor.Children.Add(footer);
        _message.Margin = new Thickness(0, 10, 0, 0); Grid.SetRow(_message, 3); workspace.Children.Add(_message);
        _addPopup.Opened += (_, _) => picker.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(picker, System.Windows.Input.KeyboardNavigationMode.Cycle);
        picker.PreviewKeyDown += (_, e) => { if (e.Key != System.Windows.Input.Key.Escape) return; _addPopup.IsOpen = false; add.Focus(); e.Handled = true; };
        SizeChanged += (_, _) => UpdateWorkspaceLayout();
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
        _overviewFrame.Visibility = Visibility.Visible;
        _galleryFrame.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateWorkspaceLayout();
    }
    private void CloseEditor()
    {
        if (!TryLeaveDraft()) return;
        _loading = true; _selected = null; _list.SelectedItem = null; _loading = false; _fields.Visibility = Visibility.Collapsed; SetEditorVisible(false);
        _preview.Child = null; _previewWindow?.Close(); _previewWindow = null;
        _save.IsEnabled = _delete.IsEnabled = _duplicate.IsEnabled = false;
        RefreshOverview(_store.Load());
    }

    private void UpdateWorkspaceLayout()
    {
        if (_workspaceBody == null) return;
        _compactWorkspace = ActualWidth < 800;
        var editing = _editorFrame.Visibility == Visibility.Visible;
        if (_workspaceLayout == (_compactWorkspace, editing)) return;
        _workspaceLayout = (_compactWorkspace, editing);
        _editorFrame.BorderThickness = _compactWorkspace ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 1, 0);
        _editorFrame.Padding = _compactWorkspace ? new Thickness(0, 12, 0, 0) : new Thickness(0, 0, 16, 0);
        var list = editing ? (FrameworkElement)_editorFrame : _galleryFrame;
        _galleryColumn.Width = _compactWorkspace ? new GridLength(1, GridUnitType.Star) : new GridLength(editing ? 320 : 280);
        _editorColumn.Width = _compactWorkspace ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(_overviewFrame, _compactWorkspace ? 0 : 1);
        Grid.SetRow(list, _compactWorkspace ? 1 : 0);
        list.Margin = _compactWorkspace ? new Thickness(0, 16, 0, 0) : new Thickness(0, 0, 16, 0);
        list.Height = double.NaN;
        _workspaceBody.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        _workspaceBody.RowDefinitions[1].Height = _compactWorkspace ? new GridLength(editing ? 1.2 : .6, GridUnitType.Star) : GridLength.Auto;
        _overviewFrame.MinHeight = 0;
        _preview.Visibility = _compactWorkspace ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string WidgetGlyph(DesktopWidgetKind kind) => kind switch
    {
        DesktopWidgetKind.Note => "\uE70B", DesktopWidgetKind.Countdown => "\uE916",
        DesktopWidgetKind.Image => "\uEB9F", DesktopWidgetKind.Link => "\uE71B",
        DesktopWidgetKind.Clock => "\uE917", DesktopWidgetKind.Visualizer => "\uE9D9", _ => "\uE8D6"
    };

    private static Style GalleryItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, 8)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, System.Windows.Application.Current.FindResource("FocusRing")));
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "Card";
        frame.SetResourceReference(StyleProperty, "InteractiveCard");
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frame.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        frame.SetValue(Border.BorderBrushProperty, System.Windows.Media.Brushes.Transparent);
        frame.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch); frame.AppendChild(presenter);
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
        content.SetValue(Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
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
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var renderer = new DesktopWidgetWindow(widget, _store);
        renderer.UpdatePreview(widget, DesktopWidgetWindow.PreviewScale(widget));
        var visual = (FrameworkElement)renderer.Content; renderer.Content = null;
        visual.Width = widget.Width; visual.Height = widget.Height; renderer.Close();
        var preview = new Viewbox { Height = 40, Stretch = Stretch.Uniform, Child = visual, IsHitTestVisible = false };
        var previewFrame = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Child = preview, Margin = new Thickness(0, 0, 8, 0) };
        previewFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush"); panel.Children.Add(previewFrame);
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        details.Children.Add(new TextBlock { Text = widget.DisplayTitle, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold, ToolTip = widget.DisplayTitle });
        var badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        badge.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        var caption = new TextBlock { Text = widget.Kind == DesktopWidgetKind.NowPlaying ? "Now playing" : widget.Kind.ToString(), FontSize = 12 };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); badge.Child = caption; details.Children.Add(badge);
        details.ToolTip = ProfileLabel(widget.ProfileId) + (widget.Enabled ? "" : " · Hidden"); Grid.SetColumn(details, 1); panel.Children.Add(details);
        var toggle = new Button { Name = "CardVisibilityButton", Content = widget.Enabled ? "\uE890" : "\uED1A", FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"),
            Width = 36, Height = 36, Padding = new Thickness(0), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = widget.Enabled ? "Hide widget" : "Show widget" };
        System.Windows.Automation.AutomationProperties.SetName(toggle, widget.Enabled ? "Hide " + widget.DisplayTitle : "Show " + widget.DisplayTitle);
        toggle.SetResourceReference(Control.ForegroundProperty, widget.Enabled ? "AccentTextBrush" : "TextTertiaryBrush");
        toggle.Click += (_, e) => { e.Handled = true; Run(() => ToggleWidget(widget)); };
        Grid.SetColumn(toggle, 2); panel.Children.Add(toggle); return panel;
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
}
