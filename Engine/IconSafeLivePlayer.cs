using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

/// <summary>
/// Icons-friendly live wallpaper: instead of an overlay window, the video (or GIF) is
/// rendered offscreen with WPF's MediaPlayer and pushed into the REAL desktop wallpaper
/// several times a second. The desktop icons and taskbar stay fully visible and behave
/// normally, at the cost of a lower effective frame rate.
/// </summary>
internal sealed class IconSafeLivePlayer : IDisposable
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(250); // ~4 fps

    private MediaPlayer? _player;
    private DispatcherTimer? _timer;
    private readonly System.Windows.Media.DrawingVisual _visual = new();
    private RenderTargetBitmap? _rtb;
    private GifBitmapDecoder? _gifDecoder;
    private int _gifFrameCount;
    private int _gifFrameIndex;
    private int _videoW;
    private int _videoH;
    private int _frameIndex;
    private string[] _framePaths = [];
    private FitMode _fit = FitMode.Fill;
    private int _generation;
    private bool _busy;
    private bool _closed;

    public void Start(string mediaPath, FitMode fitMode)
    {
        Stop();
        _fit = fitMode;
        _generation = WallpaperEngine.ApplyGeneration;
        _framePaths =
        [
            Path.Combine(AppPaths.CacheDir, "live_a.bmp"),
            Path.Combine(AppPaths.CacheDir, "live_b.bmp"),
        ];
        _frameIndex = 0;
        if (WallpaperEngine.IsAnimatedGifFile(mediaPath))
        {
            if (!LoadGifFrames(mediaPath))
            {
                Close();
                return;
            }
        }
        else
        {
            OpenVideo(mediaPath);
        }
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FrameInterval };
        _timer.Tick += (_, _) => _ = PushFrameAsync();
        _timer.Start();
        Logger.Info("Icons-friendly live wallpaper started (updating the real wallpaper).");
    }

    /// <summary>
    /// Re-activation of the same media (e.g. Save/Apply-now runs Activate twice in a row):
    /// the second Activate bumps the apply generation and this player must adopt it, or
    /// every frame push silently fails the generation check and the desktop stays static.
    /// </summary>
    public void Refresh(FitMode fitMode)
    {
        _fit = fitMode;
        _generation = WallpaperEngine.ApplyGeneration;
    }

    public void Stop()
    {
        if (_timer == null && _player == null && _gifDecoder == null)
        {
            return;
        }
        _timer?.Stop();
        _timer = null;
        try
        {
            _player?.Close();
        }
        catch
        {
        }
        _player = null;
        _gifDecoder = null;
        _gifFrameCount = 0;
        _rtb = null;
        _closed = true;
    }

    // ============ video ============

    private void OpenVideo(string path)
    {
        try
        {
            _player = new MediaPlayer();
            var opened = false;
            _player.MediaOpened += (_, _) =>
            {
                if (opened)
                {
                    return;
                }
                opened = true;
                _videoW = _player.NaturalVideoWidth;
                _videoH = _player.NaturalVideoHeight;
                if (_videoW <= 0 || _videoH <= 0)
                {
                    Logger.Error("Icons-friendly live video opened with an unknown size; stopping.");
                    Close();
                    return;
                }
                Logger.Info($"Icons-friendly live video opened ({_videoW}x{_videoH}).");
            };
            _player.MediaFailed += (_, e) =>
            {
                Logger.Error($"Icons-friendly live video failed to play: {e.ErrorException.Message}");
                Close();
            };
            _player.MediaEnded += (_, _) =>
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
            };
            _player.IsMuted = true;
            _player.Volume = 0;
            // Required for offscreen rendering: without scrubbing the player never
            // exposes fresh frames to DrawVideo, so pushes would repeat one frozen frame.
            _player.ScrubbingEnabled = true;
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }
        catch (Exception ex)
        {
            Logger.Error("Opening the icons-friendly live video failed.", ex);
            Close();
        }
    }

    // ============ GIF ============

    private bool LoadGifFrames(string path)
    {
        try
        {
            // Decode frames on demand (one per push) instead of holding every frame
            // of the GIF in memory at full size.
            _gifDecoder = new GifBitmapDecoder(new Uri(path, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnDemand);
            _gifFrameCount = _gifDecoder.Frames.Count;
            _gifFrameIndex = 0;
            if (_gifFrameCount == 0)
            {
                Logger.Error($"Icons-friendly live GIF '{path}' decoded to zero frames.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Decoding the icons-friendly live GIF '{path}' failed.", ex);
            return false;
        }
    }

    // ============ frame pushing ============

    private async Task PushFrameAsync()
    {
        if (_closed || _busy)
        {
            return;
        }
        // The real-wallpaper repaint forces every open window to repaint too — never do
        // that while the user is in a maximized app/game; just hold the last frame.
        if (DesktopLayer.IsDesktopCovered())
        {
            return;
        }
        _busy = true;
        try
        {
            BitmapSource? frame = null;
            if (_gifDecoder != null && _gifFrameCount > 0)
            {
                frame = _gifDecoder.Frames[_gifFrameIndex];
                _gifFrameIndex = (_gifFrameIndex + 1) % _gifFrameCount;
            }
            else if (_player != null && _videoW > 0)
            {
                frame = RenderVideoFrame();
            }
            if (frame == null)
            {
                return;
            }

            var target = _framePaths[_frameIndex];
            _frameIndex = (_frameIndex + 1) % _framePaths.Length;
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using (var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }
            await WallpaperEngine.SetWallpaperFrameAsync(target, _fit, _generation);
        }
        catch (Exception ex)
        {
            Logger.Error("Pushing an icons-friendly live wallpaper frame failed; stopping.", ex);
            Close();
        }
        finally
        {
            _busy = false;
        }
    }

    private BitmapSource? RenderVideoFrame()
    {
        if (_videoW <= 0 || _videoH <= 0)
        {
            return null;
        }
        // Render at the video's native size — the wallpaper API applies the fit mode,
        // so there is no reason to compose a full virtual-screen bitmap per push.
        if (_rtb == null || _rtb.PixelWidth != _videoW || _rtb.PixelHeight != _videoH)
        {
            _rtb = new RenderTargetBitmap(_videoW, _videoH, 96, 96, PixelFormats.Pbgra32);
        }
        using (var ctx = _visual.RenderOpen())
        {
            ctx.DrawRectangle(System.Windows.Media.Brushes.Black, null, new System.Windows.Rect(0, 0, _videoW, _videoH));
            ctx.DrawVideo(_player, new System.Windows.Rect(0, 0, _videoW, _videoH));
        }
        _rtb.Render(_visual);
        return _rtb;
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        Dispose();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        try
        {
            _player?.Close();
        }
        catch
        {
        }
        _player = null;
        _rtb = null;
        _gifDecoder = null;
        _gifFrameCount = 0;
        _closed = true;
    }
}
