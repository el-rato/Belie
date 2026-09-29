using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.UI;

internal static class ThumbnailLoader
{
    /// <summary>
    /// Decodes an image file into a frozen BitmapImage. Safe to call from any thread.
    /// The file handle is released immediately and the decode is capped at
    /// <paramref name="decodePixelWidth"/> so large wallpapers don't blow up memory.
    /// </summary>
    public static ImageSource? Load(string? file, int decodePixelWidth = 0)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            return null;
        }
        try
        {
            if (WallpaperEngine.IsVideoFile(file))
            {
                return VideoFrameGrabber.GrabFrame(file, decodePixelWidth > 0 ? decodePixelWidth : 512);
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(file, UriKind.Absolute);
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not load thumbnail '{file}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds the first image of a profile source (folder or single file) and decodes it.
    /// Runs entirely on the calling thread — call from a background thread.
    /// </summary>
    public static ImageSource? FirstImage(string? folder, int decodePixelWidth = 0)
    {
        var images = WallpaperEngine.GetImages(folder ?? "");
        return images.Count == 0 ? null : Load(images[0], decodePixelWidth);
    }
}
