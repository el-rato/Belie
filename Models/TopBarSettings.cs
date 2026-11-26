using System.Text.Json.Serialization;

namespace WallpaperProfiles.Models;

internal enum TopBarStyle { Solid, Islands, Pills }
internal enum TopBarEdge { Top, Bottom }
internal enum TopBarColors { Custom, Wallpaper }
internal enum WorkspaceLabelStyle { Numbers, Dots, Roman, Names, Pills }
internal enum WorkspaceShortcutModifier { None, Alt, CtrlAlt }
internal enum WorkspaceMotion { Horizontal, Vertical, Fade }
internal enum BarMediaStyle { Text, Cover, Controls, CoverOnly }
internal enum BarMediaClick { Player, PlayPause }
internal enum BarModuleKind
{
    Workspaces, Title, Clock, Date, Media, Visualizer, Cpu, Memory, Disk, Network, Volume, Battery, Uptime, Weather, Text, Launcher,
    Island, Wallpaper, Script, Separator
}

/// <summary>
/// The exact built-in actions a module may trigger without a shell, and the single source of truth for what
/// counts as safe. Review, stripping and the launcher all defer to this, so "shown to the user" and "run through
/// a shell" are the same set: anything not listed here is treated as an ordinary command, never hidden.
/// </summary>
internal static class BarActions
{
    internal static readonly IReadOnlySet<string> BuiltIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "belie:wallpapers", "belie:next-wallpaper", "belie:previous-wallpaper", "belie:settings",
        "belie:mute", "belie:volume-up", "belie:volume-down", "belie:play", "belie:next-track",
        "belie:previous-track", "belie:next-workspace", "belie:previous-workspace",
    };

    /// <summary>A recognized built-in action, carried out in-process with no shell.</summary>
    internal static bool IsBuiltIn(string command) => BuiltIn.Contains(command.Trim());

    /// <summary>Looks like a built-in action, recognized or not. An unrecognized one must never reach a shell.</summary>
    internal static bool ClaimsBuiltIn(string command) => command.TrimStart().StartsWith("belie:", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One thing shown on the bar.</summary>
internal sealed class BarModule
{
    public BarModuleKind Kind { get; set; }
    /// <summary>What a text or launcher module says.</summary>
    public string Text { get; set; } = "";
    /// <summary>The app, file or address a launcher opens, or the command a script module runs.</summary>
    public string Command { get; set; } = "";
    /// <summary>Lets a stylesheet tell modules of one kind apart: #custom-name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Replaces the module's icon. A dash shows none.</summary>
    public string Icon { get; set; } = "";
    /// <summary>What goes around the value, such as "{icon} {value}".</summary>
    public string Format { get; set; } = "";
    /// <summary>Style declarations for this module alone, named as in a stylesheet.</summary>
    public Dictionary<string, string> Style { get; set; } = new();
    /// <summary>Commands that replace what clicking and scrolling do.</summary>
    public string OnClick { get; set; } = "";
    public string OnRightClick { get; set; } = "";
    public string OnMiddleClick { get; set; } = "";
    public string OnScrollUp { get; set; } = "";
    public string OnScrollDown { get; set; } = "";
    /// <summary>How often a script runs, in seconds.</summary>
    public int Interval { get; set; } = 10;

    public BarModule() { }
    public BarModule(BarModuleKind kind) => Kind = kind;
    public BarModule Copy()
    {
        var copy = (BarModule)MemberwiseClone();
        copy.Style = new Dictionary<string, string>(Style);
        return copy;
    }

    internal void Normalize()
    {
        Text ??= ""; Command ??= ""; Name ??= ""; Icon ??= ""; Format ??= "";
        OnClick ??= ""; OnRightClick ??= ""; OnMiddleClick ??= ""; OnScrollUp ??= ""; OnScrollDown ??= "";
        Style ??= new();
        Interval = Math.Clamp(Interval, 1, 86400);
    }

    /// <summary>Everything this module would run. Only the exact built-in actions are left out; a string that merely
    /// starts with "belie:" but is not a built-in (e.g. "belie:&amp; evil") is reported, because it reaches a shell.</summary>
    internal IEnumerable<string> Commands => new[] { Command, OnClick, OnRightClick, OnMiddleClick, OnScrollUp, OnScrollDown }
        .Where(command => command.Length > 0 && !BarActions.IsBuiltIn(command));
    /// <summary>Only modules that carry their own text can appear more than once.</summary>
    internal static bool Repeats(BarModuleKind kind) => kind is BarModuleKind.Text or BarModuleKind.Launcher or BarModuleKind.Script or BarModuleKind.Separator;
}

internal sealed class TopBarSettings
{
    public const int MaxWorkspaces = 9;

    public bool Enabled { get; set; }
    public bool PrimaryMonitorOnly { get; set; }
    public TopBarEdge Edge { get; set; }
    /// <summary>The bar draws over windows and slides away while one is under it, coming back when the pointer rests at the edge.</summary>
    public bool AutoHide { get; set; } = true;
    public TopBarStyle Style { get; set; } = TopBarStyle.Islands;
    public double Height { get; set; } = 36;
    /// <summary>Space between the bar and the screen edges.</summary>
    public double BarMargin { get; set; }
    public double CornerRadius { get; set; } = 12;
    public double ModuleSpacing { get; set; } = 14;
    public double BorderWidth { get; set; }
    public string BorderColor { get; set; } = "#364239";
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 13;
    public bool Bold { get; set; }
    public bool ShowIcons { get; set; } = true;
    public TopBarColors Colors { get; set; }
    public string BackgroundColor { get; set; } = "#111613";
    public string TextColor { get; set; } = "#F3F5EC";
    public string AccentColor { get; set; } = "#DDF79D";
    public double Opacity { get; set; } = .92;

    /// <summary>What the bar shows, in order. Null only until settings from an earlier version have been converted.</summary>
    public List<BarModule>? Left { get; set; }
    public List<BarModule>? Center { get; set; }
    public List<BarModule>? Right { get; set; }

    /// <summary>Style declarations, named as in a stylesheet, which itself has the last word.</summary>
    public Dictionary<string, string> BarStyle { get; set; } = new();
    public Dictionary<string, string> LeftStyle { get; set; } = new();
    public Dictionary<string, string> CenterStyle { get; set; } = new();
    public Dictionary<string, string> RightStyle { get; set; } = new();
    /// <summary>The stylesheet, which is kept in a file of its own.</summary>
    [JsonIgnore] public string Css { get; set; } = "";

    public bool Use24Hour { get; set; } = true;
    /// <summary>A .NET time format; empty uses the usual one.</summary>
    public string ClockFormat { get; set; } = "";
    public string DateFormat { get; set; } = "";
    public BarMediaStyle MediaStyle { get; set; } = BarMediaStyle.Cover;
    public BarMediaClick MediaClick { get; set; }
    public double MediaWidth { get; set; } = 260;
    public NowPlayingLayout PlayerLayout { get; set; } = NowPlayingLayout.Cover;
    public SpectrumSkin VisualizerSkin { get; set; } = SpectrumSkin.Bars;
    public int VisualizerBars { get; set; } = 16;
    public double VisualizerWidth { get; set; } = 110;
    /// <summary>When the island grows to show more.</summary>
    public bool IslandOnTrack { get; set; } = true;
    public bool IslandOnHover { get; set; } = true;
    public bool IslandOnVolume { get; set; } = true;
    public bool IslandOnWorkspace { get; set; }
    public double IslandSeconds { get; set; } = 4;
    public double IslandWidth { get; set; } = 380;
    /// <summary>How tall the grown island is, in bar heights.</summary>
    public double IslandGrowth { get; set; } = 1.6;
    public bool IslandShowsClock { get; set; } = true;
    public bool IslandShowsMusic { get; set; } = true;
    public string WeatherPlace { get; set; } = "";
    public bool WeatherFahrenheit { get; set; }

    /// <summary>Workspaces are chosen on their own and run with or without the bar.</summary>
    [JsonPropertyName("Workspaces")] public bool WorkspacesEnabled { get; set; }
    public int WorkspaceCount { get; set; } = 5;
    public WorkspaceLabelStyle WorkspaceLabels { get; set; }
    public List<string> WorkspaceNames { get; set; } = new();
    /// <summary>The size of the drawn dots, and how long the open workspace's is.</summary>
    public double WorkspaceDot { get; set; } = 7;
    public double WorkspaceDotOpen { get; set; } = 18;
    public double WorkspaceSpacing { get; set; } = 10;
    public bool HideEmptyWorkspaces { get; set; }
    public WorkspaceShortcutModifier Shortcuts { get; set; } = WorkspaceShortcutModifier.Alt;
    /// <summary>Process names whose windows stay on every workspace.</summary>
    public List<string> PinnedApps { get; set; } = new();
    /// <summary>Profile applied when a workspace opens on the primary monitor.</summary>
    public Dictionary<int, Guid> WorkspaceProfiles { get; set; } = new();
    /// <summary>Apps, by process name, and the workspace each one opens on.</summary>
    public Dictionary<string, int> AppWorkspaces { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Sending a window to a workspace remembers its app there.</summary>
    public bool RememberAppWorkspaces { get; set; } = true;

    /// <summary>Small always-on-top windows, such as picture-in-picture players and call windows, float and stay on every workspace.</summary>
    public bool StickyPopouts { get; set; } = true;
    public bool AnimationsEnabled { get; set; } = true;
    public double AnimationSpeed { get; set; } = 1;
    public WorkspaceMotion Motion { get; set; }

    /// <summary>Until 4.0.1 workspaces ran only with the bar and were saved as on even while it was off.</summary>
    [JsonPropertyName("WorkspacesEnabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? WorkspacesWithBar { get; set; }
    // Replaced by the lists above; still read from older settings.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowTitle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowClock { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowMedia { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowCpu { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowMemory { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowVolume { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowBattery { get; set; }

    /// <summary>Whether there is anything for the bar's process to do.</summary>
    internal bool Runs => Enabled || WorkspacesEnabled;
    internal IEnumerable<BarModule> Modules => (Left ?? new()).Concat(Center ?? new()).Concat(Right ?? new());
    internal bool Shows(BarModuleKind kind) => Modules.Any(module => module.Kind == kind);

    public TopBarSettings Normalize()
    {
        if (WorkspacesWithBar is { } before) { WorkspacesEnabled |= Enabled && before; WorkspacesWithBar = null; }
        Height = Clamp(Height, 24, 64, 36);
        BarMargin = Clamp(BarMargin, 0, 32, 0);
        CornerRadius = Clamp(CornerRadius, 0, 32, 12);
        ModuleSpacing = Clamp(ModuleSpacing, 2, 48, 14);
        BorderWidth = Clamp(BorderWidth, 0, 4, 0);
        FontSize = Clamp(FontSize, 9, 24, 13);
        Opacity = Clamp(Opacity, .2, 1, .92);
        MediaWidth = Clamp(MediaWidth, 80, 600, 260);
        VisualizerBars = Math.Clamp(VisualizerBars, 3, 64);
        VisualizerWidth = Clamp(VisualizerWidth, 16, 600, 110);
        IslandSeconds = Clamp(IslandSeconds, 1, 30, 4);
        IslandWidth = Clamp(IslandWidth, 160, 900, 380);
        IslandGrowth = Clamp(IslandGrowth, 1, 4, 1.6);
        WorkspaceDot = Clamp(WorkspaceDot, 2, 32, 7);
        WorkspaceDotOpen = Clamp(WorkspaceDotOpen, 2, 96, 18);
        WorkspaceSpacing = Clamp(WorkspaceSpacing, 0, 48, 10);
        BarStyle ??= new(); LeftStyle ??= new(); CenterStyle ??= new(); RightStyle ??= new(); Css ??= "";
        AnimationSpeed = Clamp(AnimationSpeed, .25, 4, 1);
        WorkspaceCount = Math.Clamp(WorkspaceCount, 1, MaxWorkspaces);
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Segoe UI";
        if (!Enum.IsDefined(Edge)) Edge = TopBarEdge.Top;
        if (!Enum.IsDefined(Style)) Style = TopBarStyle.Islands;
        if (!Enum.IsDefined(Colors)) Colors = TopBarColors.Custom;
        if (!Enum.IsDefined(MediaStyle)) MediaStyle = BarMediaStyle.Cover;
        if (!Enum.IsDefined(MediaClick)) MediaClick = BarMediaClick.Player;
        if (!Enum.IsDefined(PlayerLayout)) PlayerLayout = NowPlayingLayout.Cover;
        if (!Enum.IsDefined(VisualizerSkin)) VisualizerSkin = SpectrumSkin.Bars;
        if (!Enum.IsDefined(Motion)) Motion = WorkspaceMotion.Horizontal;
        if (!Enum.IsDefined(WorkspaceLabels)) WorkspaceLabels = WorkspaceLabelStyle.Numbers;
        if (!Enum.IsDefined(Shortcuts)) Shortcuts = WorkspaceShortcutModifier.Alt;
        BackgroundColor ??= "#111613"; TextColor ??= "#F3F5EC"; AccentColor ??= "#DDF79D"; BorderColor ??= "#364239";
        ClockFormat ??= ""; DateFormat ??= ""; WeatherPlace ??= "";
        WorkspaceNames = (WorkspaceNames ?? new()).Take(MaxWorkspaces).Select(name => name ?? "").ToList();
        PinnedApps = Apps(PinnedApps);
        AppWorkspaces = (AppWorkspaces ?? new()).Select(item => (Name: ProcessName(item.Key ?? ""), Index: item.Value))
            .Where(item => item.Name.Length > 0 && item.Index >= 1 && item.Index <= MaxWorkspaces)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Index, StringComparer.OrdinalIgnoreCase);
        WorkspaceProfiles = (WorkspaceProfiles ?? new())
            .Where(item => item.Key is >= 1 and <= MaxWorkspaces && item.Value != Guid.Empty).ToDictionary(item => item.Key, item => item.Value);
        NormalizeModules();
        return this;
    }

    private void NormalizeModules()
    {
        Left ??= Chosen((BarModuleKind.Workspaces, true), (BarModuleKind.Title, ShowTitle));
        Center ??= Chosen((BarModuleKind.Clock, ShowClock));
        Right ??= Chosen((BarModuleKind.Media, ShowMedia), (BarModuleKind.Cpu, ShowCpu), (BarModuleKind.Memory, ShowMemory),
            (BarModuleKind.Volume, ShowVolume), (BarModuleKind.Battery, ShowBattery));
        ShowTitle = ShowClock = ShowMedia = ShowCpu = ShowMemory = ShowVolume = ShowBattery = null;
        var seen = new HashSet<BarModuleKind>();
        foreach (var list in new[] { Left, Center, Right })
        {
            list.RemoveAll(module => module == null || !Enum.IsDefined(module.Kind) || !BarModule.Repeats(module.Kind) && !seen.Add(module.Kind));
            foreach (var module in list) module.Normalize();
        }
    }

    private static List<BarModule> Chosen(params (BarModuleKind Kind, bool? Show)[] modules)
        => modules.Where(module => module.Show != false).Select(module => new BarModule(module.Kind)).ToList();

    private static List<string> Apps(List<string>? names) => (names ?? new()).Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(ProcessName).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    internal string WorkspaceName(int index) => WorkspaceNames.ElementAtOrDefault(index - 1) is { Length: > 0 } name ? name : "";

    /// <summary>The workspace a shortcut such as Alt+2 opens, or null when it is not a workspace shortcut.</summary>
    internal int? WorkspaceFor(System.Windows.Input.KeyGesture gesture)
    {
        if (!WorkspacesEnabled || Shortcuts == WorkspaceShortcutModifier.None) return null;
        var modifiers = Shortcuts == WorkspaceShortcutModifier.Alt ? System.Windows.Input.ModifierKeys.Alt
            : System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt;
        var index = gesture.Key - System.Windows.Input.Key.D0;
        return gesture.Modifiers == modifiers && index >= 1 && index <= WorkspaceCount ? index : null;
    }

    /// <summary>Accepts "spotify", "Spotify.exe" or a full path.</summary>
    internal static string ProcessName(string text)
    {
        var name = text.Trim().Trim('"').Replace('/', '\\');
        name = name[(name.LastIndexOf('\\') + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static double Clamp(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
