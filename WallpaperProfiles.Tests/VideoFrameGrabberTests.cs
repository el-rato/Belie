using System.IO;
using System.Windows.Media.Imaging;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public class VideoFrameGrabberTests
{
    private static readonly string? SampleVideo = FindSampleVideo();

    private static string? FindSampleVideo()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Untitled design.mp4"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "agentic", "agents", ".venv", "Lib", "site-packages", "gradio", "media_assets", "videos", "a.mp4"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    [Fact]
    public void GrabFrame_MissingFile_ReturnsNullWithoutCrashing()
    {
        // Regression: the old shell-thumbnail path crashed the process on bad input.
        var result = VideoFrameGrabber.GrabFrame(@"C:\definitely\missing\clip.mp4", 256);
        Assert.Null(result);
    }

    [Fact]
    public void GrabFrame_RealVideo_ReturnsFrozenFrame()
    {
        if (SampleVideo == null)
        {
            return; // No sample media on this machine; the missing-file test covers the failure path.
        }
        var result = VideoFrameGrabber.GrabFrame(SampleVideo, 256);
        var bitmap = Assert.IsType<RenderTargetBitmap>(result);
        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
        Assert.True(bitmap.IsFrozen);
    }
}
