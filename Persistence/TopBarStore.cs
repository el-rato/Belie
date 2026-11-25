using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Persistence;

/// <summary>Top bar settings. The bar lives in its own process, which is signaled after every save.</summary>
internal sealed class TopBarStore
{
    public string FilePath { get; }
    public TopBarStore(string path) => FilePath = Path.GetFullPath(path);

    public TopBarSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && AtomicJsonFile.Read<TopBarSettings>(FilePath) is { } settings)
            {
                settings.Css = LoadCss();
                return settings.Normalize();
            }
        }
        catch (Exception ex) { Logger.Error("Could not read the top bar settings; using defaults.", ex); }
        return new TopBarSettings { Css = LoadCss() };
    }

    /// <summary>The bar's stylesheet, a plain file that any editor can change.</summary>
    public string CssPath => Path.ChangeExtension(FilePath, ".css");

    public string LoadCss()
    {
        try
        {
            if (!File.Exists(CssPath)) return "";
            using var reader = new StreamReader(new FileStream(CssPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Logger.Warn("The bar's stylesheet could not be read: " + ex.Message); return ""; }
    }

    public void SaveCss(string text)
    {
        if (text.Length == 0 && !File.Exists(CssPath) || File.Exists(CssPath) && LoadCss() == text) return;
        File.WriteAllText(CssPath, text);
        Announce();
    }

    public void Save(TopBarSettings settings)
    {
        AtomicJsonFile.Write(FilePath, settings.Normalize());
        Announce();
    }

    private void Announce()
    {
        if (EventWaitHandle.TryOpenExisting(TopBarProcess.MutexName(FilePath) + ".Changed", out var changed))
        { using (changed) changed.Set(); }
    }
}
