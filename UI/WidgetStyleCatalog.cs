using WallpaperProfiles.Models;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Control = System.Windows.Controls.Control;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace WallpaperProfiles.UI;

internal static class WidgetStyles
{
    internal sealed record Entry(string Name, string Description, DesktopWidget Widget);
    internal static IReadOnlyList<Entry> Catalogue { get; } = Build();

    private static List<Entry> Build()
    {
        var entries = new List<Entry>();
        void Clock(string name, string description, ClockSkin skin, string font, string color, double size, SpectrumSkin spectrum = SpectrumSkin.Mirrored)
        {
            entries.Add(new(name, description, new DesktopWidget { Kind = DesktopWidgetKind.Clock, Title = name, ClockSkin = skin,
                FontFamily = font, FontSize = size, TextColor = color, MusicAccentColor = color, SpectrumSkin = spectrum,
                Width = skin is ClockSkin.Stacked or ClockSkin.Split ? 320 : 520, Height = skin is ClockSkin.Stacked or ClockSkin.Analog ? 400 : 280,
                ShowHeader = false, ShowBorder = false, ShowBackground = false, Alignment = WidgetTextAlignment.Center,
                ClockShowVisualizer = false, ClockShowGreeting = false }));
        }
        Clock("Sunday Script", "Flowing weekday · soft, spacious mornings", ClockSkin.Script, ClockWidgetView.ScriptFont, "#FFF1DB", 76);
        Clock("After Hours", "Editorial time · city lights and dark photography", ClockSkin.Editorial, "Bahnschrift", "#D7E9EF", 90, SpectrumSkin.Wave);
        Clock("Quiet", "Time and date · room for the wallpaper", ClockSkin.Minimal, "Segoe UI Light", "#F1F2EE", 88, SpectrumSkin.Dots);
        Clock("Monolith", "Stacked digits · architectural compositions", ClockSkin.Stacked, "Bahnschrift", "#F0E9DE", 96);
        Clock("Observatory", "Working analog dial · celestial and timeless", ClockSkin.Analog, "Georgia", "#E5DCC5", 28, SpectrumSkin.Ring);
        Clock("Signal", "Monospaced digital · retro screens and neon", ClockSkin.Digital, "Consolas", "#A3F5D2", 76, SpectrumSkin.Bars);
        Clock("Terminal", "A live time prompt · understated technical style", ClockSkin.Terminal, "Consolas", "#B8D9A6", 64, SpectrumSkin.Line);
        Clock("Parallel", "Split time and calendar · asymmetric balance", ClockSkin.Split, "Segoe UI Light", "#EDD9CE", 76, SpectrumSkin.Arc);
        Clock("In Words", "Written-out time · a softer way to read the day", ClockSkin.Words, "Georgia", "#EFE0C3", 54);
        Clock("Almanac", "Calendar-led typography · warm editorial scenes", ClockSkin.DateCard, "Georgia", "#F2D8AE", 70, SpectrumSkin.Dots);
        string[] names = { "Reflection", "Equalizer", "Tide", "Contour", "Stardust", "Halo", "Horizon", "Sunburst" };
        string[] descriptions = { "Symmetric spectrum", "Classic rising bars", "Flowing waveform", "Connected frequency contour", "A dotted spectrum", "Circular spectrum", "An open semicircle", "Radiating frequency rays" };
        var skins = Enum.GetValues<SpectrumSkin>();
        for (var i = 0; i < skins.Length; i++)
            entries.Add(new(names[i], descriptions[i], new DesktopWidget { Kind = DesktopWidgetKind.Visualizer, Title = names[i], SpectrumSkin = skins[i],
                TextColor = i % 2 == 0 ? "#D1E8DF" : "#D9C9EA", Width = 460,
                Height = skins[i] is SpectrumSkin.Ring or SpectrumSkin.Arc or SpectrumSkin.Radial ? 300 : 160,
                ShowHeader = false, ShowBorder = false, ShowBackground = false, SpectrumThickness = skins[i] == SpectrumSkin.Bars ? 4 : 2 }));
        return entries;
    }

    internal static void Apply(DesktopWidget target, DesktopWidget style)
    {
        target.ClockSkin = style.ClockSkin; target.SpectrumSkin = style.SpectrumSkin;
        target.SpectrumDensity = style.SpectrumDensity; target.SpectrumThickness = style.SpectrumThickness;
        target.SpectrumSensitivity = style.SpectrumSensitivity; target.SpectrumSmoothing = style.SpectrumSmoothing;
        target.FontFamily = style.FontFamily; target.FontSize = style.FontSize; target.Bold = style.Bold; target.Italic = style.Italic;
        target.Alignment = style.Alignment; target.TextColor = style.TextColor; target.BackgroundColor = style.BackgroundColor;
        target.BorderColor = style.BorderColor; target.BorderWidth = style.BorderWidth; target.CornerRadius = style.CornerRadius;
        target.Opacity = style.Opacity; target.ShowHeader = style.ShowHeader; target.ShowBackground = style.ShowBackground;
        target.ShowBorder = style.ShowBorder; target.GlassEffect = style.GlassEffect;
        target.ClockUse24Hour = style.ClockUse24Hour; target.ClockShowGreeting = style.ClockShowGreeting;
        target.ClockShowVisualizer = style.ClockShowVisualizer; target.MusicAccentColor = style.MusicAccentColor;
    }

}

internal sealed class WidgetStyleCatalogWindow : Window
{
    private readonly DesktopCanvasStore _store;
    private readonly string _wallpaper;
    private readonly WrapPanel _cards = new();
    private readonly TextBox _search = new() { MinWidth = 160, ToolTip = "Search styles", Margin = new Thickness(0, 0, 0, 18) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<Button> _tabs = new();
    private readonly DesktopWidgetKind? _targetKind;
    private ImageSource? _wallpaperImage;
    private readonly List<Grid> _previewTiles = new();
    private int _category;
    public DesktopWidget? SelectedStyle { get; private set; }
    public WidgetLayoutPreset? SelectedLayout { get; private set; }

    public WidgetStyleCatalogWindow(DesktopCanvasStore store, string wallpaper, DesktopWidgetKind? targetKind = null)
    {
        _store = store; _wallpaper = wallpaper; _targetKind = targetKind;
        Title = "Styles catalogue"; Width = 880; Height = 720; MinWidth = 590; MinHeight = 470;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Find the feel.", FontSize = 30, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Try a starting point. Make it yours. Save it with your wallpaper.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 20), Opacity = .7 });
        root.Children.Add(header);
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        foreach (var label in new[] { "Clocks", "Visualizers", "My styles", "Saved layouts" })
        {
            var index = _tabs.Count;
            var tab = new Button { Content = label, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(14, 8, 14, 8) };
            tab.Click += (_, _) => { _category = index; Refresh(); };
            _tabs.Add(tab); tabs.Children.Add(tab);
        }
        Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        var searchArea = new Grid(); searchArea.Children.Add(_search);
        var searchHint = new TextBlock { Text = "Search styles…", Margin = new Thickness(12, 0, 0, 18), VerticalAlignment = VerticalAlignment.Center, Opacity = .55, IsHitTestVisible = false };
        searchArea.Children.Add(searchHint);
        Grid.SetRow(searchArea, 2); root.Children.Add(searchArea);
        _search.TextChanged += (_, _) => { searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Refresh(); };
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search styles catalogue");
        var scroll = new ScrollViewer { Content = _cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 3); root.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var import = new Button { Content = "Import skin…", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(12, 0, 0, 0) };
        import.Click += (_, _) => Import(); DockPanel.SetDock(import, Dock.Right); footer.Children.Add(import);
        footer.Children.Add(_status); Grid.SetRow(footer, 4); root.Children.Add(footer);
        UiAppearance.SetDialogContent(this, root, "Styles catalogue");
        _category = targetKind is DesktopWidgetKind.Visualizer or DesktopWidgetKind.NowPlaying ? 1 : 0;
        Refresh();
        _ = LoadBackdrop();
    }

    private void Refresh()
    {
        _cards.Children.Clear();
        _previewTiles.Clear();
        for (var i = 0; i < _tabs.Count; i++) _tabs[i].SetResourceReference(Control.BackgroundProperty, i == _category ? "AccentSubtleBrush" : "SurfaceRaisedBrush");
        var document = _store.Load();
        var query = _search.Text.Trim();
        if (_category == 3)
        {
            foreach (var layout in document.LayoutPresets.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                AddCard(layout.Name, $"{layout.Widgets.Count} widgets · independent copy", null, () => { SelectedLayout = layout; DialogResult = true; }, layout);
            _status.Text = document.LayoutPresets.Count == 0 ? "Use Save layout in the studio to keep an arrangement here." : "Applying a layout replaces this canvas’s local widgets. Global widgets stay visible.";
            return;
        }
        var entries = _category == 2
            ? document.StylePresets.Select(p => new WidgetStyles.Entry(p.Name, "Your saved skin", p.Style))
            : WidgetStyles.Catalogue.Where(p => p.Widget.Kind == (_category == 0 ? DesktopWidgetKind.Clock : DesktopWidgetKind.Visualizer));
        var count = 0;
        foreach (var entry in entries.Where(p => (p.Name + " " + p.Description).Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            count++;
            AddCard(entry.Name, entry.Description, entry.Widget, () => { SelectedStyle = entry.Widget.Copy(); DialogResult = true; });
        }
        _status.Text = count == 0 ? "No styles here yet. Save a customized clock or visualizer from its Style tab."
            : _targetKind.HasValue ? "Choose a skin to preview it in the editor. Save when it feels right." : "Choose a skin to add it to your canvas. Customize before saving.";
    }

    internal void ShowLayouts() { _category = 3; Refresh(); }

    private void AddCard(string name, string description, DesktopWidget? widget, Action apply, WidgetLayoutPreset? layout = null)
    {
        var content = new StackPanel { Width = 222 };
        var preview = new Grid { Height = 146, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(20, 25, 27)) };
        _previewTiles.Add(preview);
        var wallpaper = _wallpaperImage;
        if (wallpaper != null) preview.Background = new ImageBrush(wallpaper) { Stretch = Stretch.UniformToFill };
        preview.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(95, 0, 0, 0)) });
        if (widget != null)
        {
            FrameworkElement visual;
            if (widget.Kind == DesktopWidgetKind.Clock)
                visual = new ClockWidgetView(widget) { Width = 460, Height = 280 };
            else
            {
                var spectrum = new AudioSpectrumView { PreviewMode = true, Width = 440, Height = 160 };
                spectrum.UpdateAppearance(true, new SolidColorBrush((Color)ColorConverter.ConvertFromString(widget.TextColor)), widget); visual = spectrum;
            }
            preview.Children.Add(new Viewbox { Child = visual, Stretch = Stretch.Uniform, Margin = new Thickness(14), IsHitTestVisible = false });
        }
        else if (layout != null)
        {
            var area = System.Windows.Forms.SystemInformation.VirtualScreen;
            var canvas = new System.Windows.Controls.Canvas { Width = area.Width, Height = area.Height };
            foreach (var saved in layout.Widgets)
            {
                FrameworkElement visual = saved.Kind == DesktopWidgetKind.Clock ? new ClockWidgetView(saved)
                    : new TextBlock { Text = saved.Kind == DesktopWidgetKind.Visualizer ? "▁▃▆▂▅▇▃▁" : saved.DisplayTitle, FontSize = saved.FontSize, Foreground = Brushes.White };
                var frame = new Viewbox { Width = saved.Width, Height = saved.Height, Child = visual, Stretch = Stretch.Uniform };
                System.Windows.Controls.Canvas.SetLeft(frame, saved.X - area.Left); System.Windows.Controls.Canvas.SetTop(frame, saved.Y - area.Top); canvas.Children.Add(frame);
            }
            preview.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
        }
        content.Children.Add(preview);
        content.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 5) });
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .7, Height = 34 });
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(12), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(button, "Use " + name);
        button.Click += (_, _) => apply(); _cards.Children.Add(button);
    }

    private void Import()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Belie skin (*.json)|*.json", Title = "Import a clock or visualizer skin" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 100000) throw new InvalidDataException("This skin file is too large.");
            var preset = JsonSerializer.Deserialize<WidgetStylePreset>(File.ReadAllText(picker.FileName), JsonOptions.Shared);
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 100 || preset.Style == null
                || preset.Style.Kind is not (DesktopWidgetKind.Clock or DesktopWidgetKind.Visualizer)
                || !Enum.IsDefined(preset.Style.ClockSkin) || !Enum.IsDefined(preset.Style.SpectrumSkin)
                || !SceneController.TryParseAccent(preset.Style.TextColor, out _) || !SceneController.TryParseAccent(preset.Style.BackgroundColor, out _)
                || !SceneController.TryParseAccent(preset.Style.BorderColor, out _)) throw new InvalidDataException("Choose a valid Belie clock or visualizer skin.");
            preset.Style.FontSize = double.IsFinite(preset.Style.FontSize) ? Math.Clamp(preset.Style.FontSize, 10, 96) : 36;
            preset.Style.Width = double.IsFinite(preset.Style.Width) ? Math.Clamp(preset.Style.Width, 120, 1600) : 460;
            preset.Style.Height = double.IsFinite(preset.Style.Height) ? Math.Clamp(preset.Style.Height, 80, 1200) : 280;
            preset.Style.FontFamily = string.IsNullOrWhiteSpace(preset.Style.FontFamily) ? "Segoe UI" : preset.Style.FontFamily;
            DesktopCanvasStore.NormalizeWidget(preset.Style);
            _store.Update(doc => { doc.StylePresets.RemoveAll(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase)); doc.StylePresets.Add(preset); });
            _category = 2; Refresh();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private async Task LoadBackdrop()
    {
        var image = await Task.Run(() => ThumbnailLoader.Load(_wallpaper, 900)).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted) return;
        await Dispatcher.InvokeAsync(() =>
        {
            _wallpaperImage = image;
            if (image != null) foreach (var tile in _previewTiles) tile.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        });
    }
}
