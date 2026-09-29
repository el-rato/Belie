using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WallpaperProfiles.UI;

/// <summary>
/// Grabs a preview frame from a video file using WPF's MediaPlayer (DrawVideo into a
/// RenderTargetBitmap). Entirely managed — deliberately NOT using the shell's
/// IShellItemImageFactory, whose native video thumbnail handlers have crashed the app
/// with AccessViolationException from background threads.
/// Runs its work on a dedicated STA thread; returns null on any failure.
/// </summary>
internal static class VideoFrameGrabber
{
    // Both hits and misses are cached: path validation repeats while the user types.
    private static readonly ConcurrentDictionary<(string Path, int Width), ImageSource?> Cache = new();

    public static ImageSource? GrabFrame(string videoPath, int maxWidth)
    {
        var key = (Path.GetFullPath(videoPath).ToUpperInvariant(), maxWidth);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var result = GrabFrameOnStaThread(videoPath, maxWidth);
        Cache[key] = result;
        return result;
    }

    private static ImageSource? GrabFrameOnStaThread(string videoPath, int maxWidth)
    {
        ImageSource? result = null;
        var thread = new Thread(() => result = GrabFrameCore(videoPath, maxWidth))
        {
            IsBackground = true,
            Name = "VideoFrameGrabber",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(7));
        return result;
    }

    private static ImageSource? GrabFrameCore(string videoPath, int maxWidth)
    {
        var player = new MediaPlayer();
        ImageSource? result = null;
        try
        {
            var done = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                done.Continue = false;
            };

            player.MediaFailed += (_, _) =>
            {
                timer.Stop();
                done.Continue = false;
            };
            player.MediaOpened += (_, _) =>
            {
                try
                {
                    player.Position = SeekTarget(player);
                    // Give the decoder a moment to produce the seeked frame.
                    var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    settle.Tick += (_, _) =>
                    {
                        settle.Stop();
                        timer.Stop();
                        result = RenderCurrentFrame(player, maxWidth);
                        done.Continue = false;
                    };
                    settle.Start();
                }
                catch
                {
                    timer.Stop();
                    done.Continue = false;
                }
            };

            player.ScrubbingEnabled = true;
            player.IsMuted = true;
            player.Volume = 0;
            player.Open(new Uri(videoPath, UriKind.Absolute));
            timer.Start();
            Dispatcher.PushFrame(done);
        }
        catch
        {
            result = null;
        }
        finally
        {
            player.Close();
        }
        return result;
    }

    private static TimeSpan SeekTarget(MediaPlayer player)
    {
        if (player.NaturalDuration.HasTimeSpan)
        {
            var total = player.NaturalDuration.TimeSpan.TotalSeconds;
            return TimeSpan.FromSeconds(Math.Min(1.0, Math.Max(0.0, total * 0.25)));
        }
        return TimeSpan.FromSeconds(0);
    }

    private static ImageSource? RenderCurrentFrame(MediaPlayer player, int maxWidth)
    {
        try
        {
            var width = player.NaturalVideoWidth;
            var height = player.NaturalVideoHeight;
            if (width <= 0 || height <= 0)
            {
                return null;
            }
            var scale = maxWidth > 0 && width > maxWidth ? (double)maxWidth / width : 1.0;
            var w = Math.Max(1, (int)Math.Round(width * scale));
            var h = Math.Max(1, (int)Math.Round(height * scale));
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawVideo(player, new System.Windows.Rect(0, 0, w, h));
            }
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
