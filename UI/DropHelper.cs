using System.IO;
using WallpaperProfiles.Engine;

namespace WallpaperProfiles.UI;

/// <summary>
/// Extracts a usable wallpaper source (image, video, or folder) from drag-and-drop data.
/// </summary>
internal static class DropHelper
{
    public const string ImageFileFilter =
        "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.jfif|All files|*.*";

    public const string VideoFileFilter =
        "Videos|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mpg;*.mpeg;*.m2v;*.mkv;*.webm;*.3gp|All files|*.*";

    /// <summary>Returns the first accepted path in the data object, or null.</summary>
    public static string? ExtractPath(System.Windows.IDataObject data)
    {
        if (data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            if (data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var first = files[0];
                if (Directory.Exists(first) || (File.Exists(first) && IsMediaFile(first)))
                {
                    return first;
                }
            }
            return null;
        }

        if (data.GetDataPresent(System.Windows.DataFormats.StringFormat)
            && data.GetData(System.Windows.DataFormats.StringFormat) is string text
            && !string.IsNullOrWhiteSpace(text)
            && (Directory.Exists(text) || (File.Exists(text) && IsMediaFile(text))))
        {
            return text;
        }

        return null;
    }

    public static bool CanAccept(System.Windows.IDataObject data) => ExtractPath(data) != null;

    private static bool IsMediaFile(string path)
        => WallpaperEngine.IsImageFile(path) || WallpaperEngine.IsVideoFile(path);
}

/// <summary>
/// Inspects a candidate wallpaper source (folder or single media file) off the UI thread.
/// </summary>
internal static class PathInspector
{
    public static (bool Exists, bool IsFile, int ImageCount, int VideoCount, string? FirstMedia) Inspect(string path)
    {
        if (File.Exists(path))
        {
            return WallpaperEngine.IsImageFile(path) ? (true, true, 1, 0, path)
                 : WallpaperEngine.IsVideoFile(path) ? (true, true, 0, 1, path)
                 : (true, true, 0, 0, null);
        }
        if (Directory.Exists(path))
        {
            var entries = WallpaperEngine.GetImages(path);
            var videos = 0;
            try
            {
                videos = Directory.EnumerateFiles(path)
                    .Count(f => WallpaperEngine.IsVideoFile(f));
            }
            catch (Exception)
            {
            }
            return (true, false, entries.Count, videos, entries.Count > 0 ? entries[0] : null);
        }
        return (false, false, 0, 0, null);
    }
}
