using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace WallpaperProfiles.Engine;

/// <summary>A background, text and accent color that suit a picture.</summary>
internal sealed record WallpaperColors(Color Background, Color Text, Color Accent)
{
    public static WallpaperColors? Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var image = Image.FromStream(stream, false, false);
        return From(Sample(image, 96, 54));
    }

    internal static int[] Sample(Image image, int width, int height)
    {
        using var small = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(image, 0, 0, width, height);
        }
        var data = small.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        var pixels = new int[width * height];
        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        small.UnlockBits(data);
        for (var i = 0; i < pixels.Length; i++) pixels[i] &= 0xFFFFFF;
        return pixels;
    }

    internal static int[] Sample(BitmapSource image, int width, int height)
    {
        var scaled = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(width / (double)image.PixelWidth, height / (double)image.PixelHeight));
        var converted = new FormatConvertedBitmap(scaled, System.Windows.Media.PixelFormats.Bgr32, null, 0);
        var pixels = new int[converted.PixelWidth * converted.PixelHeight];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        for (var i = 0; i < pixels.Length; i++) pixels[i] &= 0xFFFFFF;
        return pixels;
    }

    internal static WallpaperColors From(int[] pixels)
    {
        var strong = WallpaperTransition.Vivid(pixels);
        // Light enough to read on a dark bar.
        var accent = Mix(Color.FromRgb(strong.R, strong.G, strong.B), System.Windows.Media.Colors.White, .25);
        var average = Color.FromRgb((byte)pixels.Average(pixel => pixel >> 16 & 255), (byte)pixels.Average(pixel => pixel >> 8 & 255), (byte)pixels.Average(pixel => pixel & 255));
        var dark = Mix(Mix(average, accent, .12), Color.FromRgb(14, 15, 18), .78);
        return new WallpaperColors(dark, Mix(accent, System.Windows.Media.Colors.White, .85), accent);
    }

    internal static Color Mix(Color color, Color other, double amount) => Color.FromRgb((byte)(color.R + (other.R - color.R) * amount),
        (byte)(color.G + (other.G - color.G) * amount), (byte)(color.B + (other.B - color.B) * amount));
}
