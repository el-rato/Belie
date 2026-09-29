using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Engine;

/// <summary>
/// One installed Wallpaper Engine workshop wallpaper (steamapps\workshop\content\431960\&lt;id&gt;).
/// </summary>
internal sealed record WorkshopItem(
    string ProjectDir,
    string ProjectJsonPath,
    string Title,
    string ContentType,
    string? MediaFile,
    string? PreviewFile)
{
    /// <summary>True when this app can play it directly (a video file our host supports).</summary>
    public bool Playable => MediaFile != null && WallpaperEngine.IsVideoFile(MediaFile);
}

/// <summary>
/// Read-only access to a Wallpaper Engine (Steam app 431960) installation:
/// discovers Steam libraries, lists the workshop wallpapers from their
/// project.json files, and can drive the Wallpaper Engine app itself for
/// content types this app cannot render (scene/web/application).
/// </summary>
internal static partial class WallpaperEngineWorkshop
{
    public const int SteamAppId = 431960;

    private static readonly string[] PreviewNames = ["preview.jpg", "preview.png", "preview.jpeg"];

    [GeneratedRegex(@"""path""\s+""([^""]+)""")]
    private static partial Regex LibraryPathRegex();

    public static string? FindSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not read the Steam registry key: {ex.Message}");
            return null;
        }
    }

    public static List<string> FindLibraryRoots()
    {
        var roots = new List<string>();
        var steam = FindSteamPath();
        if (string.IsNullOrWhiteSpace(steam))
        {
            return roots;
        }
        roots.Add(steam);
        try
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match match in LibraryPathRegex().Matches(File.ReadAllText(vdf)))
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (!roots.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        roots.Add(path);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not parse Steam's libraryfolders.vdf: {ex.Message}");
        }
        return roots;
    }

    /// <summary>The first Steam library that contains Wallpaper Engine workshop content, or null.</summary>
    public static string? FindWorkshopRoot()
    {
        foreach (var root in FindLibraryRoots())
        {
            var workshop = Path.Combine(root, "steamapps", "workshop", "content", SteamAppId.ToString());
            if (Directory.Exists(workshop))
            {
                return workshop;
            }
        }
        return null;
    }

    /// <summary>The Wallpaper Engine app executable (wallpaper64.exe / wallpaper32.exe), or null.</summary>
    public static string? FindWallpaperEngineExe()
    {
        foreach (var root in FindLibraryRoots())
        {
            var bin = Path.Combine(root, "steamapps", "common", "wallpaper_engine", "bin");
            var x64 = Path.Combine(bin, "wallpaper64.exe");
            if (File.Exists(x64))
            {
                return x64;
            }
            var x86 = Path.Combine(bin, "wallpaper32.exe");
            if (File.Exists(x86))
            {
                return x86;
            }
        }
        return null;
    }

    /// <summary>
    /// Parses every project.json under the workshop root. Bad items are skipped, never thrown.
    /// Pass an explicit <paramref name="workshopRoot"/> to scan a different folder (tests).
    /// </summary>
    public static List<WorkshopItem> ListItems(string? workshopRoot = null)
    {
        var items = new List<WorkshopItem>();
        var root = workshopRoot ?? FindWorkshopRoot();
        if (root == null || !Directory.Exists(root))
        {
            return items;
        }
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var item = ParseItem(dir);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Skipping Wallpaper Engine item '{dir}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Listing the Wallpaper Engine workshop failed.", ex);
        }
        return items;
    }

    private static WorkshopItem? ParseItem(string dir)
    {
        var jsonPath = Path.Combine(dir, "project.json");
        if (!File.Exists(jsonPath))
        {
            return null;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = root.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
            ? titleEl.GetString() ?? ""
            : "";
        if (title.Length == 0)
        {
            title = Path.GetFileName(dir);
        }

        var contentType = "";
        if (root.TryGetProperty("contenttype", out var contentTypeEl) && contentTypeEl.ValueKind == JsonValueKind.String)
        {
            contentType = contentTypeEl.GetString() ?? "";
        }
        if (contentType.Length == 0
            && root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            contentType = typeEl.GetString() ?? "";
        }

        string? mediaFile = null;
        if (root.TryGetProperty("file", out var fileEl) && fileEl.ValueKind == JsonValueKind.String
            && fileEl.GetString() is { Length: > 0 } relative)
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(dir, relative));
                if (File.Exists(candidate))
                {
                    mediaFile = candidate;
                }
            }
            catch (Exception)
            {
                // Odd characters in the stored path — treat as missing.
            }
        }

        var previewFile = PreviewNames
            .Select(name => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);

        return new WorkshopItem(dir, jsonPath, title, contentType, mediaFile, previewFile);
    }

    /// <summary>
    /// Asks the Wallpaper Engine app to show a wallpaper this app cannot render itself
    /// (scene/web/application content). Requires Wallpaper Engine to be installed.
    /// </summary>
    public static bool TryOpenViaWallpaperEngine(string projectJsonPath)
    {
        var exe = FindWallpaperEngineExe();
        if (exe == null)
        {
            Logger.Warn("Wallpaper Engine is not installed; cannot open the item with it.");
            return false;
        }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-control openWallpaper -file \"{projectJsonPath}\"",
                UseShellExecute = false,
            };
            Process.Start(psi);
            Logger.Info($"Asked Wallpaper Engine to open '{projectJsonPath}'.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Launching Wallpaper Engine failed.", ex);
            return false;
        }
    }
}
