using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Persistence;

namespace WallpaperProfiles.Workspaces;

/// <summary>
/// The windows Belie has hidden, written before they are hidden. A hidden window has no taskbar button,
/// so this file is what lets a later process bring it back after a crash.
/// </summary>
internal sealed class HiddenWindowJournal
{
    private readonly string _path;
    public HiddenWindowJournal(string path) => _path = Path.GetFullPath(path);

    public static string PathFor(string settingsFile) => Path.ChangeExtension(Path.GetFullPath(settingsFile), ".hidden.json");

    public void Save(IReadOnlyCollection<WindowIdentity> windows)
    {
        if (windows.Count == 0 && !File.Exists(_path)) return;
        AtomicJsonFile.Write(_path, windows);
    }

    public IReadOnlyList<WindowIdentity> Load()
    {
        try { return File.Exists(_path) ? AtomicJsonFile.Read<List<WindowIdentity>>(_path) ?? new() : new List<WindowIdentity>(); }
        catch (Exception ex) { Logger.Error("Could not read the hidden window list.", ex); return Array.Empty<WindowIdentity>(); }
    }

    /// <summary>Shows windows left hidden by a host that ended unexpectedly. Returns how many were brought back.</summary>
    public int RestoreOrphans(IWindowSystem system)
    {
        var restored = 0;
        foreach (var window in Load())
        {
            if (!system.Exists(window) || system.IsVisible((nint)window.Handle)) continue;
            system.Show((nint)window.Handle);
            restored++;
        }
        if (restored > 0) Logger.Warn($"Brought back {restored} window(s) left hidden by a previous session.");
        Save(Array.Empty<WindowIdentity>());
        return restored;
    }
}
