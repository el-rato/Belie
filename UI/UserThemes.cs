using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using Color = System.Windows.Media.Color;

namespace WallpaperProfiles.UI;

/// <summary>
/// Themes people write themselves: one JSON file each in the themes folder. Only the background, text and accent are
/// needed; every other colour is worked out from those three unless the file gives it. Files are read again as soon
/// as they change, so a theme can be tuned while Belie is open.
/// </summary>
internal static class UserThemes
{
    public static string Folder => Path.Combine(AppPaths.BaseDir, "themes");
    public const string ExampleFile = "_example.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
    };

    private sealed class ThemeFile
    {
        public string? Name { get; set; }
        public string? Background { get; set; }
        public string? Text { get; set; }
        public string? Accent { get; set; }
        public string? Surface { get; set; }
        public string? Raised { get; set; }
        public string? Hover { get; set; }
        public string? Border { get; set; }
        public string? StrongBorder { get; set; }
        public string? Secondary { get; set; }
        public string? Muted { get; set; }
        public string? Subtle { get; set; }
        public string? Danger { get; set; }
        public string? Good { get; set; }
        public string? Signal { get; set; }
        public bool? Light { get; set; }
    }

    /// <summary>Reads one theme file. Throws <see cref="FormatException"/> with a message meant for the person.</summary>
    internal static UiTheme Parse(string json, string fileName)
    {
        ThemeFile file;
        try { file = JsonSerializer.Deserialize<ThemeFile>(json, Options) ?? throw new FormatException("The file is empty."); }
        catch (JsonException ex) { throw new FormatException($"It is not valid JSON (line {ex.LineNumber + 1})."); }

        Color Required(string? value, string key) => value == null ? throw new FormatException($"\"{key}\" is missing.")
            : HexColor.TryParse(value.Trim(), out var color) ? color : throw new FormatException($"\"{key}\" must look like #1E1E2E.");
        Color Optional(string? value, string key, Color fallback) => value == null ? fallback
            : HexColor.TryParse(value.Trim(), out var color) ? color : throw new FormatException($"\"{key}\" must look like #1E1E2E.");

        var background = Required(file.Background, "background");
        var text = Required(file.Text, "text");
        var accent = Required(file.Accent, "accent");
        var name = string.IsNullOrWhiteSpace(file.Name) ? Path.GetFileNameWithoutExtension(fileName) : file.Name.Trim();
        if (name.Length > 40) throw new FormatException("\"name\" is longer than 40 characters.");
        string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        string Mixed(string? value, string key, Color from, Color to, double amount) => Hex(Optional(value, key, UiAppearance.Mix(from, to, amount)));
        string? Extra(string? value, string key) => value == null ? null : Hex(Optional(value, key, default));

        return new UiTheme(name,
            Hex(background),
            Mixed(file.Surface, "surface", background, text, .04),
            Mixed(file.Raised, "raised", background, text, .09),
            Mixed(file.Hover, "hover", background, text, .15),
            Mixed(file.Border, "border", background, text, .18),
            Mixed(file.StrongBorder, "strongBorder", background, text, .42),
            Hex(text),
            Mixed(file.Secondary, "secondary", text, background, .2),
            Mixed(file.Muted, "muted", text, background, .36),
            Hex(accent),
            Mixed(file.Subtle, "subtle", accent, background, .78))
        {
            Danger = Extra(file.Danger, "danger"), Good = Extra(file.Good, "good"), Signal = Extra(file.Signal, "signal"),
            IsLight = file.Light, FileName = Path.GetFileName(fileName)
        };
    }

    /// <summary>Every theme in <paramref name="folder"/>, and what was wrong with the files that could not be used.</summary>
    internal static (List<UiTheme> Themes, List<string> Problems) Read(string folder, IReadOnlyCollection<string> builtIn)
    {
        var themes = new List<UiTheme>();
        var problems = new List<string>();
        if (!Directory.Exists(folder)) return (themes, problems);
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith('_')) continue; // Notes and examples.
            try
            {
                var theme = Parse(File.ReadAllText(path), fileName);
                if (builtIn.Contains(theme.Name, StringComparer.OrdinalIgnoreCase))
                    problems.Add($"{fileName}: \"{theme.Name}\" is already a built-in theme. Give it another name.");
                else if (themes.Any(other => other.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"{fileName}: another file already uses the name \"{theme.Name}\".");
                else themes.Add(theme);
            }
            catch (FormatException ex) { problems.Add($"{fileName}: {ex.Message}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add($"{fileName}: could not be read ({ex.Message})."); }
        }
        return (themes, problems);
    }

    private const string Example = """
        // An example Belie theme. Copy this file, give the copy a name without the "_" in front
        // (for example gruvbox.json) and change the colours. Belie picks it up straight away and
        // lists it under Settings > Appearance. Files that start with "_" are ignored.
        //
        // Only "background", "text" and "accent" are needed. Every other colour is worked out
        // from those three, but you can set any of them yourself:
        //   surface, raised, hover       panels and cards, from darkest to lightest
        //   border, strongBorder         edges
        //   secondary, muted             quieter text
        //   subtle                       the tint behind selected things
        //   danger, good, signal         errors, success and warnings
        //   light                        true for a light theme (worked out when left out)
        {
          "name": "Gruvbox",
          "background": "#1D2021",
          "text": "#EBDBB2",
          "accent": "#FABD2F"
        }
        """;

    private static FileSystemWatcher? _watcher;
    private static DispatcherTimer? _reload;

    /// <summary>Loads the themes folder and keeps watching it. Call once, on the UI thread.</summary>
    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var example = Path.Combine(Folder, ExampleFile);
            if (!File.Exists(example)) File.WriteAllText(example, Example);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Logger.Error("The themes folder could not be created.", ex); }
        UiAppearance.LoadUserThemes(Folder);
        if (!Directory.Exists(Folder)) return;
        _reload = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _reload.Tick += (_, _) => { _reload.Stop(); UiAppearance.LoadUserThemes(Folder); };
        var dispatcher = Dispatcher.CurrentDispatcher;
        _watcher = new FileSystemWatcher(Folder, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        void Queue() => dispatcher.BeginInvoke(() => { _reload.Stop(); _reload.Start(); });
        _watcher.Changed += (_, _) => Queue(); _watcher.Created += (_, _) => Queue();
        _watcher.Deleted += (_, _) => Queue(); _watcher.Renamed += (_, _) => Queue();
        _watcher.EnableRaisingEvents = true;
    }
}
