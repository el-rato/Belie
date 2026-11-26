using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace WallpaperProfiles.UI;

/// <summary>Both pages edit the same file, which the bar's own process watches.</summary>
internal abstract class BarSettingsPage : SettingsPage<TopBarSettings>
{
    private readonly TopBarStore _store;
    private readonly Action<string> _start;
    protected TopBarStore Bar => _store;

    protected BarSettingsPage(string title, string description, TopBarStore store, Action<string>? start) : base(title, description)
    {
        _store = store;
        _start = start ?? TopBarProcess.Start;
    }

    protected override TopBarSettings Load() => _store.Load().Normalize();

    protected override void Store(TopBarSettings settings)
    {
        _store.Save(settings);
        if (settings.Runs) _start(_store.FilePath);
    }

    protected static List<string> Apps(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
}

internal sealed partial class TopBarPage : BarSettingsPage
{
    private static readonly (BarModuleKind Kind, string Name, string About)[] ModuleNames =
    {
        (BarModuleKind.Workspaces, "Workspaces", "Your workspaces, to click or scroll"), (BarModuleKind.Title, "Window title", "The window you are using"),
        (BarModuleKind.Clock, "Clock", "The time; click for a calendar"), (BarModuleKind.Date, "Date", "Today; click for a calendar"),
        (BarModuleKind.Media, "Music player", "What is playing; click for the player"), (BarModuleKind.Visualizer, "Visualizer", "Bars that move with the sound"),
        (BarModuleKind.Cpu, "Processor", "How busy the processor is"), (BarModuleKind.Memory, "Memory", "How much memory is in use"),
        (BarModuleKind.Disk, "Disk", "How full the system drive is"), (BarModuleKind.Network, "Network", "Download and upload speed"),
        (BarModuleKind.Volume, "Volume", "Scroll to change, click to mute"), (BarModuleKind.Battery, "Battery", "Charge, when there is a battery"),
        (BarModuleKind.Uptime, "Uptime", "Time since Windows started"), (BarModuleKind.Weather, "Weather", "Now; click for the forecast"),
        (BarModuleKind.Island, "Island", "Clock and music that grow when something happens"), (BarModuleKind.Wallpaper, "Wallpaper", "Opens the switcher; scroll to go through wallpapers"),
        (BarModuleKind.Text, "Text", "Your own words or symbol"), (BarModuleKind.Launcher, "Launcher", "Opens an app, file or address"),
        (BarModuleKind.Script, "Script", "Shows what a command of yours writes"), (BarModuleKind.Separator, "Separator", "A line or symbol between modules")
    };

    private static readonly (string Name, string Left, string Center, string Right)[] Sets =
    {
        ("Essentials", "Workspaces Title", "Clock", "Volume Battery"),
        ("Music", "Workspaces Media", "Clock", "Visualizer Volume"),
        ("System monitor", "Workspaces Title", "Clock Date", "Cpu Memory Disk Network Battery"),
        ("Everything", "Workspaces Title", "Clock Date Weather", "Media Visualizer Network Cpu Memory Disk Volume Battery")
    };

    private readonly List<BarModule>[] _modules = { new(), new(), new() };
    private readonly WrapPanel[] _rows = { new(), new(), new() };

    public TopBarPage(TopBarStore store, Action<string>? start = null)
        : base("Top bar", "Pick what the bar shows and how it looks. Changes appear on the bar straight away.", store, start)
    {
        StartLooks();
        Reload();
    }

    internal static string NameOf(BarModuleKind kind) => ModuleNames.First(item => item.Kind == kind).Name;

    protected override void Build(TopBarSettings settings)
    {
        BuildLooks(settings);
        Tab("Modules");
        if (!settings.WorkspacesEnabled && settings.Shows(BarModuleKind.Workspaces))
            Hint("Workspaces are off, so their module stays hidden. They can be turned on under Workspaces.");
        Switch("Primary monitor only", "TopBarPrimaryOnly", settings.PrimaryMonitorOnly, (to, value) => to.PrimaryMonitorOnly = value);
        Heading("On the bar");
        Hint("The arrows move a module along the bar, and on into the next part. Click its name to change how it looks and what it does.");
        var lists = new[] { settings.Left!, settings.Center!, settings.Right! };
        for (var part = 0; part < 3; part++)
        {
            var index = part;
            _modules[part] = lists[part].Select(module => module.Copy()).ToList();
            var name = new[] { "Left", "Center", "Right" }[part];
            var add = Press("+  Add", "TopBarAdd" + name, () => { });
            add.Click += (_, _) => OpenAddMenu(add, index);
            add.VerticalAlignment = VerticalAlignment.Top;
            var header = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
            DockPanel.SetDock(add, Dock.Right);
            header.Children.Add(add);
            header.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            _rows[part] = new WrapPanel { MinHeight = 44 };
            var body = new StackPanel(); body.Children.Add(header); body.Children.Add(_rows[part]);
            var card = new Border { Child = body, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 10), Margin = new Thickness(0, 0, 0, 10) };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            Add(card);
        }
        ShowModules();
        OnSave(to => (to.Left, to.Center, to.Right) = (Copy(0), Copy(1), Copy(2)));
        _edited = null; _editor.Children.Clear();
        Detach(_editor); Add(_editor);
        Heading("Start from a set");
        var sets = new WrapPanel();
        foreach (var set in Sets) sets.Children.Add(Press(set.Name, "TopBarSet" + set.Name.Replace(" ", ""), () => UseSet(set)));
        Add(sets);

        Tab("Style");
        Pick("Position", "TopBarEdge", new[] { new Choice<TopBarEdge>(TopBarEdge.Top, "Top"), new(TopBarEdge.Bottom, "Bottom") }, settings.Edge, (to, value) => to.Edge = value);
        Pick("Style", "TopBarStyle", new[] { new Choice<TopBarStyle>(TopBarStyle.Islands, "Floating islands"), new(TopBarStyle.Pills, "Separate pills"), new(TopBarStyle.Solid, "Solid bar") },
            settings.Style, (to, value) => to.Style = value);
        Slide("Height", "TopBarHeight", 24, 64, 1, settings.Height, " px", (to, value) => to.Height = value);
        Slide("Space around the bar", "TopBarMargin", 0, 32, 1, settings.BarMargin, " px", (to, value) => to.BarMargin = value);
        Slide("Roundness", "TopBarRadius", 0, 32, 1, settings.CornerRadius, " px", (to, value) => to.CornerRadius = value);
        Slide("Space between modules", "TopBarSpacing", 2, 48, 1, settings.ModuleSpacing, " px", (to, value) => to.ModuleSpacing = value);
        Slide("Border", "TopBarBorder", 0, 4, .5, settings.BorderWidth, " px", (to, value) => to.BorderWidth = value);
        Heading("Text");
        var fonts = FontBox(settings.FontFamily, _ => Changed());
        OnSave(to => to.FontFamily = fonts.Text.Trim());
        Row("Font", "TopBarFont", fonts);
        Slide("Text size", "TopBarFontSize", 9, 24, .5, settings.FontSize, " pt", (to, value) => to.FontSize = value);
        Switch("Bold", "TopBarBold", settings.Bold, (to, value) => to.Bold = value);
        Switch("Icons", "TopBarIcons", settings.ShowIcons, (to, value) => to.ShowIcons = value);
        Heading("Colors");
        Pick("Colors from", "TopBarColors", new[] { new Choice<TopBarColors>(TopBarColors.Custom, "The choices below"), new(TopBarColors.Wallpaper, "The wallpaper") },
            settings.Colors, (to, value) => to.Colors = value);
        var background = Color("Background", "TopBarBackground", settings.BackgroundColor, (to, value) => to.BackgroundColor = value);
        var text = Color("Text", "TopBarText", settings.TextColor, (to, value) => to.TextColor = value);
        var accent = Color("Accent", "TopBarAccent", settings.AccentColor, (to, value) => to.AccentColor = value);
        var border = Color("Border color", "TopBarBorderColor", settings.BorderColor, (to, value) => to.BorderColor = value);
        Slide("Opacity", "TopBarOpacity", 20, 100, 1, Math.Round(settings.Opacity * 100), "%", (to, value) => to.Opacity = value / 100);
        Hint("Palettes");
        var palettes = new WrapPanel();
        foreach (var theme in UiAppearance.Themes)
        {
            Border Dot(string color) => new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 3, 0),
                Background = (SolidColorBrush)new BrushConverter().ConvertFromString(color)! };
            var face = new StackPanel { Orientation = Orientation.Horizontal };
            face.Children.Add(Dot(theme.Background)); face.Children.Add(Dot(theme.Accent));
            face.Children.Add(new TextBlock { Text = theme.Name, FontSize = 12, Margin = new Thickness(4, 0, 0, 0) });
            var button = Press("", "TopBarPalette" + theme.Name, () =>
            { (background.Text, text.Text, accent.Text, border.Text) = (theme.Background, theme.Text, theme.Accent, theme.Border); });
            button.Content = face; AutomationProperties.SetName(button, theme.Name + " palette");
            palettes.Children.Add(button);
        }
        Add(palettes);
        BuildParts(settings);

        Tab("Clock and music");
        Switch("24-hour time", "TopBarUse24Hour", settings.Use24Hour, (to, value) => to.Use24Hour = value);
        Text("Clock format", "TopBarClockFormat", settings.ClockFormat, (to, value) => to.ClockFormat = Format(value, "Clock format"), "HH:mm:ss, or ddd HH:mm");
        Text("Date format", "TopBarDateFormat", settings.DateFormat, (to, value) => to.DateFormat = Format(value, "Date format"), "ddd d MMM");
        Hint("Leave a format empty for the usual one.");
        Heading("Music");
        Pick("Show as", "TopBarMediaStyle", new[] { new Choice<BarMediaStyle>(BarMediaStyle.Cover, "Cover and title"), new(BarMediaStyle.Text, "Title only"), new(BarMediaStyle.Controls, "Cover, title and buttons"), new(BarMediaStyle.CoverOnly, "Cover only") },
            settings.MediaStyle, (to, value) => to.MediaStyle = value);
        Slide("Width", "TopBarMediaWidth", 80, 600, 10, settings.MediaWidth, " px", (to, value) => to.MediaWidth = value);
        Pick("Click", "TopBarMediaClick", new[] { new Choice<BarMediaClick>(BarMediaClick.Player, "Opens the player"), new(BarMediaClick.PlayPause, "Plays or pauses") },
            settings.MediaClick, (to, value) => to.MediaClick = value);
        Pick("Player", "TopBarPlayer", Enum.GetValues<NowPlayingLayout>().Select(layout => new Choice<NowPlayingLayout>(layout, PlayerName(layout))),
            settings.PlayerLayout, (to, value) => to.PlayerLayout = value);
        Pick("Visualizer", "TopBarVisualizer", Enum.GetValues<SpectrumSkin>().Where(skin => skin is not (SpectrumSkin.Ring or SpectrumSkin.Arc or SpectrumSkin.Radial))
            .Select(skin => new Choice<SpectrumSkin>(skin, skin.ToString())), settings.VisualizerSkin, (to, value) => to.VisualizerSkin = value);
        Slide("Visualizer bars", "TopBarVisualizerBars", 3, 64, 1, settings.VisualizerBars, "", (to, value) => to.VisualizerBars = (int)Math.Round(value));
        Slide("Visualizer width", "TopBarVisualizerWidth", 16, 400, 2, settings.VisualizerWidth, " px", (to, value) => to.VisualizerWidth = value);
        Heading("Weather");
        Text("Place", "TopBarWeatherPlace", settings.WeatherPlace, (to, value) => to.WeatherPlace = value, "Lisbon", whenDone: true);
        Switch("Fahrenheit", "TopBarFahrenheit", settings.WeatherFahrenheit, (to, value) => to.WeatherFahrenheit = value);
        Hint("Only while a weather module or widget is in use, Belie sends this place name to open-meteo.com to look up its weather. Leave it empty to send nothing.");

        Tab("Island");
        if (!settings.Shows(BarModuleKind.Island)) Hint("Add the Island module to the bar to see these.");
        Switch("Clock", "IslandClock", settings.IslandShowsClock, (to, value) => to.IslandShowsClock = value);
        Switch("Music and visualizer", "IslandMusic", settings.IslandShowsMusic, (to, value) => to.IslandShowsMusic = value);
        Heading("Grows");
        Switch("When the track changes", "IslandOnTrack", settings.IslandOnTrack, (to, value) => to.IslandOnTrack = value);
        Switch("When you point at it", "IslandOnHover", settings.IslandOnHover, (to, value) => to.IslandOnHover = value);
        Switch("When the volume changes", "IslandOnVolume", settings.IslandOnVolume, (to, value) => to.IslandOnVolume = value);
        Switch("When the workspace changes", "IslandOnWorkspace", settings.IslandOnWorkspace, (to, value) => to.IslandOnWorkspace = value);
        Slide("Stays grown for", "IslandSeconds", 1, 15, .5, settings.IslandSeconds, " s", (to, value) => to.IslandSeconds = value);
        Slide("Grown width", "IslandWidth", 160, 900, 10, settings.IslandWidth, " px", (to, value) => to.IslandWidth = value);
        Slide("Grown height", "IslandGrowth", 1, 4, .1, settings.IslandGrowth, " × bar", (to, value) => to.IslandGrowth = value);

        BuildStylesheet();
    }

    internal static string PlayerName(NowPlayingLayout layout) => layout switch
    {
        NowPlayingLayout.Cover => "Full cover", NowPlayingLayout.Vinyl => "Vinyl record", NowPlayingLayout.Ring => "Progress ring", _ => layout.ToString()
    };

    private static string Format(string format, string label)
    {
        if (format.Length == 0) return format;
        try { DateTime.Now.ToString(format.Length == 1 ? "%" + format : format, CultureInfo.CurrentCulture); return format; }
        catch (FormatException) { throw new FormatException(label + " is not a time format."); }
    }

    private List<BarModule> Copy(int part) => _modules[part].Select(module => module.Copy()).ToList();

    private void UseSet((string Name, string Left, string Center, string Right) set)
    {
        var parts = new[] { set.Left, set.Center, set.Right };
        for (var part = 0; part < 3; part++)
            _modules[part] = parts[part].Split(' ').Select(name => new BarModule(Enum.Parse<BarModuleKind>(name))).ToList();
        ShowModules(); Changed();
    }

    private void OpenAddMenu(Button button, int part)
    {
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var item in Available())
        {
            var entry = new System.Windows.Controls.MenuItem { Header = item.Name, ToolTip = item.About };
            entry.Click += (_, _) => AddModule(item.Kind, part);
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
    }

    internal IEnumerable<(BarModuleKind Kind, string Name, string About)> Available()
        => ModuleNames.Where(item => BarModule.Repeats(item.Kind) || !_modules.SelectMany(list => list).Any(module => module.Kind == item.Kind));

    internal IReadOnlyList<BarModule> Modules(int part) => _modules[part];

    internal void AddModule(BarModuleKind kind, int part)
    {
        _modules[part].Add(new BarModule(kind) { Text = kind == BarModuleKind.Text ? "Hello" : kind == BarModuleKind.Launcher ? "Explorer" : "", Command = kind == BarModuleKind.Launcher ? "explorer.exe" : kind == BarModuleKind.Script ? "echo Hello" : "" });
        ShowModules(); Changed();
    }

    /// <summary>Along its part, and past the end into the next one.</summary>
    internal void Move(int part, int index, int by)
    {
        var module = _modules[part][index];
        var target = index + by;
        if (target >= 0 && target < _modules[part].Count)
            (_modules[part][index], _modules[part][target]) = (_modules[part][target], module);
        else if (part + by is >= 0 and < 3)
        {
            _modules[part].RemoveAt(index);
            _modules[part + by].Insert(by > 0 ? 0 : _modules[part + by].Count, module);
        }
        else return;
        ShowModules(); Changed();
    }

    private void ShowModules()
    {
        if (_edited != null && !_modules.Any(list => list.Contains(_edited))) Edit(null);
        for (var part = 0; part < 3; part++)
        {
            _rows[part].Children.Clear();
            if (_modules[part].Count == 0)
            {
                var empty = new TextBlock { Text = "Nothing here", VerticalAlignment = VerticalAlignment.Center };
                empty.SetResourceReference(StyleProperty, "HintText");
                _rows[part].Children.Add(empty);
            }
            for (var i = 0; i < _modules[part].Count; i++) _rows[part].Children.Add(Chip(part, i));
        }
    }

    private FrameworkElement Chip(int part, int index)
    {
        var module = _modules[part][index];
        var name = NameOf(module.Kind);
        Button Small(string glyph, string label, Action click, bool enabled = true)
        {
            var button = new Button { Content = glyph, Width = 28, Height = 28, MinHeight = 0, Padding = new Thickness(0), FontSize = 10, BorderThickness = new Thickness(0), IsEnabled = enabled, ToolTip = label };
            button.SetResourceReference(FontFamilyProperty, "IconFont");
            AutomationProperties.SetName(button, label + ": " + name);
            button.Click += (_, _) => click();
            return button;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Small("", "Move left", () => Move(part, index, -1), part > 0 || index > 0));
        var open = new Button { Content = module.Text.Length > 0 ? module.Text : name, FontSize = 13, MinHeight = 0, Height = 28, Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0),
            ToolTip = ModuleNames.First(item => item.Kind == module.Kind).About + ". Click to change it." };
        AutomationProperties.SetName(open, "Change: " + name);
        open.Click += (_, _) => { Edit(_edited == module ? null : module); _editor.BringIntoView(); };
        row.Children.Add(open);
        row.Children.Add(Small("", "Move right", () => Move(part, index, 1), part < 2 || index < _modules[part].Count - 1));
        row.Children.Add(Small("", "Remove", () => { _modules[part].RemoveAt(index); ShowModules(); Changed(); }));
        var chip = new Border { Child = row, CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Margin = new Thickness(0, 0, 8, 8) };
        chip.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        return chip;
    }
}

internal sealed class WorkspacesPage : BarSettingsPage
{
    private sealed record ProfileChoice(Guid Id, string Name) { public override string ToString() => Name; }
    private readonly Func<IReadOnlyList<WallpaperProfile>> _profiles;

    public WorkspacesPage(TopBarStore store, Func<IReadOnlyList<WallpaperProfile>> profiles, Action<string>? start = null)
        : base("Workspaces", "Each monitor has its own workspaces. Windows on the others are hidden and leave the taskbar until you return.", store, start)
    {
        _profiles = profiles;
        Reload();
    }

    protected override void Build(TopBarSettings settings)
    {
        var profiles = _profiles();
        Tab("Workspaces");
        Switch("Use workspaces", "TopBarWorkspaces", settings.WorkspacesEnabled, (to, value) => to.WorkspacesEnabled = value);
        Hint("Optional, and separate from the top bar. Without the bar, workspaces are opened with the shortcuts below. Tiling has its own switch under Tiling.");
        var count = Pick("Number of workspaces", "TopBarWorkspaceCount",
            Enumerable.Range(1, TopBarSettings.MaxWorkspaces).Select(number => new Choice<int>(number, number.ToString(CultureInfo.CurrentCulture))),
            settings.WorkspaceCount, (to, value) => to.WorkspaceCount = value);
        Pick("Labels", "TopBarLabels", new[] { new Choice<WorkspaceLabelStyle>(WorkspaceLabelStyle.Numbers, "Numbers"), new(WorkspaceLabelStyle.Dots, "Dots"),
            new(WorkspaceLabelStyle.Pills, "Dots and a pill"), new(WorkspaceLabelStyle.Roman, "Roman numerals"), new(WorkspaceLabelStyle.Names, "Names") }, settings.WorkspaceLabels, (to, value) => to.WorkspaceLabels = value);
        Slide("Dot size", "TopBarDot", 2, 24, 1, settings.WorkspaceDot, " px", (to, value) => to.WorkspaceDot = value);
        Slide("Open workspace's length", "TopBarDotOpen", 2, 60, 1, settings.WorkspaceDotOpen, " px", (to, value) => to.WorkspaceDotOpen = value);
        Slide("Space between dots", "TopBarDotSpacing", 0, 40, 1, settings.WorkspaceSpacing, " px", (to, value) => to.WorkspaceSpacing = value);
        Switch("Hide empty workspaces on the bar", "TopBarHideEmpty", settings.HideEmptyWorkspaces, (to, value) => to.HideEmptyWorkspaces = value);
        Pick("Shortcuts", "TopBarShortcuts", new[]
        {
            new Choice<WorkspaceShortcutModifier>(WorkspaceShortcutModifier.Alt, "Alt + number"),
            new(WorkspaceShortcutModifier.CtrlAlt, "Ctrl + Alt + number"), new(WorkspaceShortcutModifier.None, "Off")
        }, settings.Shortcuts, (to, value) => to.Shortcuts = value);
        Hint("Add Shift to send the current window to that workspace.");
        var shared = profiles.Where(profile => ProfileShortcutManager.TryParse(profile.KeyboardShortcut ?? "", out var gesture, out _)
            && new TopBarSettings { WorkspacesEnabled = true, WorkspaceCount = TopBarSettings.MaxWorkspaces, Shortcuts = WorkspaceShortcutModifier.Alt }.WorkspaceFor(gesture!) != null)
            .Select(profile => $"{profile.KeyboardShortcut} ({profile.Name})").ToArray();
        if (shared.Length > 0) Hint($"Also profile shortcuts: {string.Join(", ", shared)}. These open the workspace, then apply its profile below, or the shortcut's own if none is chosen.");
        Text("Apps that stay on every workspace", "TopBarPinnedApps", string.Join(", ", settings.PinnedApps), (to, value) => to.PinnedApps = Apps(value), "Spotify, Discord");

        Heading("Names and profiles");
        Hint("A profile is applied when its workspace opens on the primary monitor. Belie must be running in the tray.");
        var choices = new[] { new ProfileChoice(Guid.Empty, "Keep current profile") }.Concat(profiles.Select(profile => new ProfileChoice(profile.Id, profile.Name))).ToArray();
        var names = new TextBox[TopBarSettings.MaxWorkspaces]; var boxes = new ComboBox[TopBarSettings.MaxWorkspaces]; var rows = new DockPanel[TopBarSettings.MaxWorkspaces];
        for (var i = 0; i < rows.Length; i++)
        {
            names[i] = new TextBox { Text = settings.WorkspaceName(i + 1), Width = 130, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Name" };
            AutomationProperties.SetAutomationId(names[i], "TopBarName" + (i + 1)); AutomationProperties.SetName(names[i], $"Workspace {i + 1} name");
            var selected = settings.WorkspaceProfiles.GetValueOrDefault(i + 1);
            boxes[i] = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected) ?? choices[0], Width = 200 };
            AutomationProperties.SetAutomationId(boxes[i], "TopBarProfile" + (i + 1)); AutomationProperties.SetName(boxes[i], $"Workspace {i + 1} profile");
            names[i].TextChanged += (_, _) => Changed(); boxes[i].SelectionChanged += (_, _) => Changed();
            var pair = new StackPanel { Orientation = Orientation.Horizontal };
            pair.Children.Add(names[i]); pair.Children.Add(boxes[i]);
            rows[i] = Row($"Workspace {i + 1}", "TopBarWorkspaceRow" + (i + 1), pair);
        }
        void ShowRows() { if (count.SelectedItem is Choice<int> chosen) for (var i = 0; i < rows.Length; i++) rows[i].Visibility = i < chosen.Value ? Visibility.Visible : Visibility.Collapsed; }
        count.SelectionChanged += (_, _) => ShowRows();
        ShowRows();
        OnSave(to =>
        {
            to.WorkspaceNames = names.Select(box => box.Text.Trim()).ToList();
            to.WorkspaceProfiles = Enumerable.Range(0, boxes.Length).Where(i => boxes[i].SelectedItem is ProfileChoice choice && choice.Id != Guid.Empty)
                .ToDictionary(i => i + 1, i => ((ProfileChoice)boxes[i].SelectedItem).Id);
        });

        Tab("Tiling");
        Switch("Tile windows automatically", "TopBarTiling", settings.TilingEnabled, (to, value) => to.TilingEnabled = value);
        Pick("Layout", "TopBarTileLayout", new[]
        {
            new Choice<TileLayout>(TileLayout.Dwindle, "Dwindle: each window splits the one beside it"), new(TileLayout.Grid, "Grid: even tiles, for many windows"),
            new(TileLayout.Columns, "Columns: side by side"), new(TileLayout.MainAndStack, "Main window and a stack")
        }, settings.TileLayout, (to, value) => to.TileLayout = value);
        Hint("There is no limit on how many windows tile. Every layout fills the screen and turns with it, so columns become rows on a portrait monitor.");
        Slide("Gap between windows", "TopBarTileGap", 0, 64, 1, settings.TileGap, " px", (to, value) => to.TileGap = (int)Math.Round(value));
        Slide("Gap at screen edges", "TopBarTileMargin", 0, 64, 1, settings.TileMargin, " px", (to, value) => to.TileMargin = (int)Math.Round(value));
        Switch("No gaps around a window that is alone", "TopBarSmartGaps", settings.SmartGaps, (to, value) => to.SmartGaps = value);
        Slide("First window's share", "TopBarSplitRatio", 20, 80, 5, Math.Round(settings.SplitRatio * 100), "%", (to, value) => to.SplitRatio = value / 100);
        Hint("In Dwindle, the share a window keeps when another opens beside it. In Main window and a stack, the main window's share.");
        Switch("Stretch fixed-size windows too", "TopBarTileFixedSize", settings.TileFixedSize, (to, value) => to.TileFixedSize = value);
        Hint("Windows that can't be resized by their edges are sized to their tile anyway. One that stays smaller is centered in its tile, and one that can't get smaller gets more room.");
        Switch("Keep popout players and calls floating on every workspace", "TopBarStickyPopouts", settings.StickyPopouts, (to, value) => to.StickyPopouts = value);
        Hint("Picture-in-picture videos and small always-on-top windows, such as mini players and call windows, are never tiled or stretched. With this on, they also stay visible when you switch workspaces.");
        Text("Apps that always float", "TopBarFloatingApps", string.Join(", ", settings.FloatingApps), (to, value) => to.FloatingApps = Apps(value), "Calculator, obs64");
        Heading("Keys");
        Hint("Ctrl + Alt + arrows move focus. Add Shift to swap windows. Ctrl + Alt + F floats a window, and Ctrl + Alt + J turns its split in Dwindle. Drag a window onto another to swap them, or drag an edge to resize.");

        Tab("Animation");
        Switch("Animate workspaces and windows", "TopBarAnimations", settings.AnimationsEnabled, (to, value) => to.AnimationsEnabled = value);
        Pick("Workspace change", "TopBarMotion", new[] { new Choice<WorkspaceMotion>(WorkspaceMotion.Horizontal, "Slide sideways"), new(WorkspaceMotion.Vertical, "Slide up and down"), new(WorkspaceMotion.Fade, "Fade") },
            settings.Motion, (to, value) => to.Motion = value);
        Slide("Speed", "TopBarAnimationSpeed", 25, 400, 25, Math.Round(settings.AnimationSpeed * 100), "%", (to, value) => to.AnimationSpeed = value / 100);
    }
}
