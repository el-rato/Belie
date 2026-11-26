using System.Text.Json;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;

namespace WallpaperProfiles.Engine;

/// <summary>A complete look for the bar: what is on it, how it is drawn and its stylesheet.</summary>
internal sealed class BarRice
{
    public string Name { get; set; } = "";
    public string About { get; set; } = "";
    public TopBarSettings Look { get; set; } = new();
    private string _css = "";
    /// <summary>With the same line endings wherever the look was written or built.</summary>
    public string Css { get => _css; set => _css = value?.ReplaceLineEndings("\n")!; }

    /// <summary>Takes the look, and leaves alone how workspaces, tiles and the bar itself behave.</summary>
    public TopBarSettings ApplyTo(TopBarSettings current)
    {
        var next = JsonSerializer.Deserialize<TopBarSettings>(JsonSerializer.Serialize(Look, JsonOptions.Shared), JsonOptions.Shared)!;
        next.Css = Css;
        next.Enabled = current.Enabled; next.PrimaryMonitorOnly = current.PrimaryMonitorOnly; next.Edge = current.Edge;
        next.Use24Hour = current.Use24Hour; next.WeatherPlace = current.WeatherPlace; next.WeatherFahrenheit = current.WeatherFahrenheit;
        next.MediaClick = current.MediaClick; next.PlayerLayout = current.PlayerLayout;
        next.WorkspacesWithBar = null; // From a look made before 4.0.2.
        next.WorkspacesEnabled = current.WorkspacesEnabled; next.WorkspaceCount = current.WorkspaceCount; next.WorkspaceNames = current.WorkspaceNames;
        next.HideEmptyWorkspaces = current.HideEmptyWorkspaces; next.Shortcuts = current.Shortcuts; next.PinnedApps = current.PinnedApps; next.WorkspaceProfiles = current.WorkspaceProfiles;
        next.StickyPopouts = current.StickyPopouts; next.AutoHide = current.AutoHide;
        next.AppWorkspaces = current.AppWorkspaces; next.RememberAppWorkspaces = current.RememberAppWorkspaces;
        next.AnimationsEnabled = current.AnimationsEnabled; next.AnimationSpeed = current.AnimationSpeed; next.Motion = current.Motion;
        return next.Normalize();
    }

    public static BarRice From(string name, TopBarSettings settings) => new() { Name = name, Look = settings, Css = settings.Css };

    /// <summary>Everything the look would run, for someone to read before they accept a look made by somebody else.</summary>
    public IReadOnlyList<string> Commands => Look.Normalize().Modules.SelectMany(module => module.Commands).Distinct().ToArray();

    public void RemoveCommands()
    {
        foreach (var list in new[] { Look.Normalize().Left!, Look.Center!, Look.Right! })
        {
            list.RemoveAll(module => module.Kind is BarModuleKind.Script or BarModuleKind.Launcher && module.Commands.Any());
            foreach (var module in list)
            {
                // Only Belie's own recognized actions are harmless; anything else, "belie:"-looking or not, is cleared.
                static string Own(string command) => BarActions.IsBuiltIn(command) ? command : "";
                module.Command = Own(module.Command); module.OnClick = Own(module.OnClick); module.OnRightClick = Own(module.OnRightClick);
                module.OnMiddleClick = Own(module.OnMiddleClick); module.OnScrollUp = Own(module.OnScrollUp); module.OnScrollDown = Own(module.OnScrollDown);
            }
        }
    }

    private static BarModule M(BarModuleKind kind, string icon = "", string format = "", string color = "", string name = "", string text = "")
    {
        var module = new BarModule(kind) { Icon = icon, Format = format, Name = name, Text = text };
        if (color.Length > 0) module.Style["color"] = color;
        return module;
    }

    private static List<BarModule> List(params BarModule[] modules) => modules.ToList();

    /// <summary>Belie's own looks.</summary>
    public static IReadOnlyList<BarRice> BuiltIn => new BarRice[]
    {
        new()
        {
            Name = "Island", About = "A floating island with the clock and the music, colored by the wallpaper. It grows when the track changes.",
            Look = new TopBarSettings
            {
                Style = TopBarStyle.Islands, Colors = TopBarColors.Wallpaper, Height = 38, FontFamily = "Silkscreen, Press Start 2P, VT323, Cascadia Mono, Consolas", FontSize = 14, ModuleSpacing = 16, Opacity = 1,
                WorkspaceLabels = WorkspaceLabelStyle.Pills, WorkspaceDot = 7, WorkspaceDotOpen = 18, WorkspaceSpacing = 12, VisualizerSkin = SpectrumSkin.Mirrored, VisualizerBars = 14,
                Left = List(M(BarModuleKind.Workspaces)), Center = List(M(BarModuleKind.Island)),
                Right = List(M(BarModuleKind.Volume, format: "{icon}"), M(BarModuleKind.Network, format: "{icon}"), M(BarModuleKind.Wallpaper))
            },
            Css = """
                /* Nothing behind the sides; the island floats on its own. */
                .left, .center, .right { background: transparent; shape: none; padding: 0 6px; }
                #island { background: #0c0d10; height: 28px; font-weight: bold; }
                #volume icon { color: #e0645c; }
                #workspaces button.empty, #workspaces button.occupied { background: #a9b2bd; }
                """
        },
        new()
        {
            Name = "Notch", About = "A notch joined to the top of the screen, with plain text on both sides.",
            Look = new TopBarSettings
            {
                Colors = TopBarColors.Wallpaper, Height = 34, FontFamily = "Segoe UI Variable Text, Segoe UI", FontSize = 13, ModuleSpacing = 18, Opacity = 1, CornerRadius = 14,
                WorkspaceLabels = WorkspaceLabelStyle.Pills, WorkspaceDot = 8, WorkspaceDotOpen = 24,
                Left = List(M(BarModuleKind.Workspaces), M(BarModuleKind.Title)), Center = List(M(BarModuleKind.Clock), M(BarModuleKind.Visualizer)), VisualizerBars = 9, VisualizerWidth = 54, VisualizerSkin = SpectrumSkin.Mirrored,
                ClockFormat = "ddd d MMM  HH:mm", Right = List(M(BarModuleKind.Media), M(BarModuleKind.Volume), M(BarModuleKind.Battery), M(BarModuleKind.Wallpaper))
            },
            Css = """
                .left, .right { background: transparent; shape: none; }
                .center { shape: notch; background: #0b0c0f; border-radius: 14px; padding: 0 18px; }
                #title { opacity: .7; }
                """
        },
        new()
        {
            Name = "Mocha pills", About = "Soft separate pills, each with a pastel color of its own.",
            Look = new TopBarSettings
            {
                Style = TopBarStyle.Pills, Height = 40, BarMargin = 4, CornerRadius = 14, FontFamily = "JetBrainsMono Nerd Font, JetBrains Mono, Cascadia Code, Consolas", FontSize = 13, ModuleSpacing = 12, Opacity = 1,
                BackgroundColor = "#1e1e2e", TextColor = "#cdd6f4", AccentColor = "#cba6f7", BorderColor = "#313244",
                Left = List(M(BarModuleKind.Workspaces), M(BarModuleKind.Title)), Center = List(M(BarModuleKind.Clock), M(BarModuleKind.Media)),
                Right = List(M(BarModuleKind.Cpu), M(BarModuleKind.Memory), M(BarModuleKind.Network), M(BarModuleKind.Volume), M(BarModuleKind.Battery), M(BarModuleKind.Wallpaper))
            },
            Css = """
                @define-color blue #89b4fa;  @define-color green #a6e3a1;  @define-color peach #fab387;
                @define-color red #f38ba8;   @define-color teal #94e2d5;   @define-color yellow #f9e2af;
                .module { margin: 0 1px; }
                #clock   { color: #1e1e2e; background: linear-gradient(90deg, @accent, @blue); font-weight: bold; }
                #cpu     { color: @blue; }    #memory { color: @green; }   #network { color: @teal; }
                #volume  { color: @peach; }   #battery { color: @yellow; } #wallpaper { color: @red; }
                #media   { color: @green; }   #title { color: alpha(@text, .75); }
                .module:hover { background: #313244; }
                #clock:hover { background: linear-gradient(90deg, @blue, @accent); }
                """
        },
        new()
        {
            Name = "Blocks", About = "Square, solid blocks of warm color, edge to edge.",
            Look = new TopBarSettings
            {
                Style = TopBarStyle.Solid, Height = 30, CornerRadius = 0, FontFamily = "Iosevka, Cascadia Mono, Consolas", FontSize = 13, Bold = true, ModuleSpacing = 2, Opacity = 1,
                BackgroundColor = "#282828", TextColor = "#ebdbb2", AccentColor = "#fabd2f", BorderColor = "#504945",
                Left = List(M(BarModuleKind.Workspaces), M(BarModuleKind.Title)), Center = List(M(BarModuleKind.Clock)),
                Right = List(M(BarModuleKind.Cpu), M(BarModuleKind.Memory), M(BarModuleKind.Disk), M(BarModuleKind.Volume), M(BarModuleKind.Battery)),
                ClockFormat = "HH:mm  ddd d"
            },
            Css = """
                part { padding: 0; }
                #cpu, #memory, #disk, #volume, #battery, #clock { padding: 0 12px; color: #282828; icon-color: #282828; }
                #cpu { background: #83a598; }     #memory { background: #8ec07c; }   #disk { background: #b8bb26; }
                #volume { background: #fabd2f; }  #battery { background: #fe8019; }  #clock { background: #d3869b; }
                #workspaces { padding: 0 4px; }   #title { padding: 0 10px; color: #a89984; }
                """
        },
        new()
        {
            Name = "Glass", About = "One floating sheet of dark glass with a colored rim.",
            Look = new TopBarSettings
            {
                Style = TopBarStyle.Solid, Height = 38, BarMargin = 8, CornerRadius = 18, BorderWidth = 1.5, Colors = TopBarColors.Wallpaper, FontFamily = "Segoe UI Variable Text, Segoe UI", FontSize = 13, ModuleSpacing = 16, Opacity = .55,
                WorkspaceLabels = WorkspaceLabelStyle.Pills, WorkspaceDot = 9, WorkspaceDotOpen = 26,
                Left = List(M(BarModuleKind.Workspaces), M(BarModuleKind.Separator), M(BarModuleKind.Title)), Center = List(M(BarModuleKind.Clock)),
                Right = List(M(BarModuleKind.Media), M(BarModuleKind.Separator), M(BarModuleKind.Network), M(BarModuleKind.Volume), M(BarModuleKind.Battery), M(BarModuleKind.Wallpaper))
            },
            Css = """
                bar { background: alpha(@background, .55); border: 1.5px solid alpha(@accent, .6); padding: 0 10px; }
                #clock { font-weight: bold; font-size: 15px; }
                """
        },
        new()
        {
            Name = "Terminal", About = "Green letters in square brackets on black.",
            Look = new TopBarSettings
            {
                Style = TopBarStyle.Solid, Height = 28, CornerRadius = 0, FontFamily = "VT323, Cascadia Mono, Consolas", FontSize = 14, ShowIcons = false, ModuleSpacing = 10, Opacity = 1, BorderWidth = 1,
                BackgroundColor = "#050805", TextColor = "#4ee06a", AccentColor = "#4ee06a", BorderColor = "#1d4a28", WorkspaceLabels = WorkspaceLabelStyle.Numbers, MediaStyle = BarMediaStyle.Text, ClockFormat = "'['HH:mm:ss']'",
                Left = List(M(BarModuleKind.Workspaces), M(BarModuleKind.Title, format: "~/{value}")), Center = List(M(BarModuleKind.Clock)),
                Right = List(M(BarModuleKind.Cpu, format: "[cpu {value}]"), M(BarModuleKind.Memory, format: "[mem {value}]"), M(BarModuleKind.Network, format: "[{value}]"), M(BarModuleKind.Volume, format: "[vol {value}]"), M(BarModuleKind.Battery, format: "[bat {value}]"))
            },
            Css = """
                #title { opacity: .6; }
                #workspaces button.active { color: #050805; background: @accent; }
                """
        },
        new()
        {
            Name = "Minimal", About = "Only words, straight on the wallpaper.",
            Look = new TopBarSettings
            {
                Style = TopBarStyle.Solid, Height = 30, FontFamily = "Segoe UI Variable Display, Segoe UI", FontSize = 13, ShowIcons = false, ModuleSpacing = 14, Opacity = .2, Colors = TopBarColors.Wallpaper, WorkspaceLabels = WorkspaceLabelStyle.Roman, MediaStyle = BarMediaStyle.Text,
                Left = List(M(BarModuleKind.Workspaces)), Center = List(M(BarModuleKind.Clock)), Right = List(M(BarModuleKind.Media), M(BarModuleKind.Separator, text: "·"), M(BarModuleKind.Volume), M(BarModuleKind.Wallpaper))
            },
            Css = """
                bar { background: linear-gradient(180deg, rgba(0,0,0,.45), transparent); border: none; padding: 0 12px; }
                #clock { font-weight: 600; }
                #workspaces button.active { background: transparent; color: @accent; }
                """
        },
        new()
        {
            Name = "Neon", About = "Three dark islands with glowing gradient rims.",
            Look = new TopBarSettings
            {
                Style = TopBarStyle.Islands, Height = 40, BarMargin = 5, CornerRadius = 17, FontFamily = "Orbitron, Bahnschrift, Segoe UI", FontSize = 13, ModuleSpacing = 16, Opacity = 1, BorderWidth = 2,
                BackgroundColor = "#0d0a1a", TextColor = "#f1eaff", AccentColor = "#ff4fd8", BorderColor = "#5ad1ff", WorkspaceLabels = WorkspaceLabelStyle.Pills, WorkspaceDot = 8, WorkspaceDotOpen = 26,
                Left = List(M(BarModuleKind.Workspaces)), Center = List(M(BarModuleKind.Clock), M(BarModuleKind.Visualizer)), Right = List(M(BarModuleKind.Media), M(BarModuleKind.Cpu), M(BarModuleKind.Memory), M(BarModuleKind.Volume)),
                VisualizerSkin = SpectrumSkin.Mirrored, VisualizerBars = 20, VisualizerWidth = 110
            },
            Css = """
                part { border: 2px solid linear-gradient(90deg, #ff4fd8, #5ad1ff); padding: 0 16px; }
                #clock { color: #5ad1ff; font-weight: bold; }
                #visualizer { color: linear-gradient(90deg, #5ad1ff, #ff4fd8); }
                #workspaces button.active { background: linear-gradient(90deg, #ff4fd8, #5ad1ff); }
                """
        }
    };
}

/// <summary>The looks someone has saved, each in a file that can be shared.</summary>
internal sealed class BarRiceStore
{
    public const string Extension = ".bar.json";
    public string Folder { get; }
    public BarRiceStore(string folder) => Folder = folder;

    public IReadOnlyList<(string Path, BarRice Rice)> Load()
    {
        var rices = new List<(string, BarRice)>();
        if (!Directory.Exists(Folder)) return rices;
        foreach (var path in Directory.EnumerateFiles(Folder, "*" + Extension).OrderBy(path => path))
            try { rices.Add((path, Read(path))); }
            catch (Exception ex) { Logger.Warn($"The look in {path} could not be read: {ex.Message}"); }
        return rices;
    }

    public static BarRice Read(string path)
    {
        if (new FileInfo(path).Length > 1_000_000) throw new InvalidDataException("This file is too large to be a look.");
        var rice = AtomicJsonFile.Read<BarRice>(path) ?? throw new InvalidDataException("This file has no look in it.");
        rice.Look = (rice.Look ?? throw new InvalidDataException("This file has no look in it.")).Normalize();
        rice.Css ??= ""; rice.About ??= "";
        if (string.IsNullOrWhiteSpace(rice.Name)) rice.Name = Path.GetFileName(path).Replace(Extension, "", StringComparison.OrdinalIgnoreCase);
        return rice;
    }

    public string Save(BarRice rice)
    {
        Directory.CreateDirectory(Folder);
        var name = string.Concat(rice.Name.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
        var path = Path.Combine(Folder, (name.Length > 0 ? name : "Look") + Extension);
        Write(path, rice);
        return path;
    }

    /// <summary>Leaves out what is about its owner rather than the look.</summary>
    public static void Write(string path, BarRice rice)
    {
        var clean = new BarRice { Name = rice.Name, About = rice.About, Css = rice.Css, Look = rice.ApplyTo(new TopBarSettings()) };
        AtomicJsonFile.Write(path, clean);
    }
}
