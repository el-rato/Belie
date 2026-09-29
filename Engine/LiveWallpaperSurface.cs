using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace WallpaperProfiles.Engine;

/// <summary>
/// The live wallpaper surface: a plain WinForms window re-parented onto the desktop's
/// wallpaper layer, painted with GDI. WPF's MediaPlayer is used ONLY as an offscreen
/// decoder (DrawVideo into a RenderTargetBitmap, then blitted) — WPF never presents to
/// the window itself, because its DWM/layered presentation paths stop working after the
/// window is re-parented (frames rendered with zero alpha or not at all). GDI into a
/// child window always composites correctly.
/// The surface owns a dedicated STA thread with its own Dispatcher: decoding, rendering,
/// and the WinForms message pump all run there, so the app's UI thread is never blocked
/// by frame work (and the render loop keeps a steady pace even while the UI is busy).
/// Handles both video files and animated GIFs.
/// </summary>
internal sealed class LiveWallpaperSurface : IDisposable
{
    private readonly string _mediaPath;
    private readonly FitMode _fit;
    private readonly bool _muted;
    private readonly Action? _onClosed;
    private readonly ManualResetEventSlim _attachSignal = new(false);
    private readonly ManualResetEvent _exited = new(false);

    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private volatile bool _closed;
    private int _attachOk;
    private int _onClosedInvoked;

    // ===== touched only on the surface thread =====
    private WallpaperForm _form = null!;
    private System.Drawing.Graphics? _graphics;
    private MediaPlayer? _player;
    private DrawingVisual? _visual;
    private DispatcherTimer? _renderTimer;
    private DispatcherTimer? _gifTimer;
    private DispatcherTimer? _visibilityTimer;
    private bool _desktopCovered;
    private bool _playerPausedForCover;
    private int _rendering;
    private RenderTargetBitmap? _rtb;
    private System.Drawing.Bitmap? _gdiFrame;
    private System.Drawing.Bitmap? _presentBmp;
    private System.Drawing.Graphics? _presentGfx;
    private GifBitmapDecoder? _gifDecoder;
    private int _gifFrameCount;
    private TimeSpan[] _gifDelays = [];
    private int _gifIndex;
    private int _videoW;
    private int _videoH;
    private bool _renderLoopLogged;

    public LiveWallpaperSurface(string mediaPath, FitMode fitMode, bool muted, Action? onClosed)
    {
        _mediaPath = mediaPath;
        _fit = fitMode;
        _muted = muted;
        _onClosed = onClosed;
    }

    private sealed class WallpaperForm : System.Windows.Forms.Form
    {
        public WallpaperForm()
        {
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            BackColor = System.Drawing.Color.Black;
            SetStyle(System.Windows.Forms.ControlStyles.Opaque, true);
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == NativeMethods.WM_DISPLAYCHANGE && IsHandleCreated)
            {
                DesktopLayer.RepositionForDisplayChange(Handle);
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>Signals when the surface thread has fully exited (attach failed, self-closed, or disposed).</summary>
    public WaitHandle ExitedHandle => _exited;

    /// <summary>
    /// Starts the surface thread, attaches the window to the desktop and starts playback.
    /// Returns false (and cleans itself up) when the desktop attach failed.
    /// </summary>
    public bool Show()
    {
        _thread = new Thread(ThreadProc)
        {
            Name = "LiveWallpaperSurface",
            IsBackground = true,
            // Never compete with the user's apps: the wallpaper yields CPU first so a
            // maximized browser/game stays smooth even while video decodes behind it.
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_attachSignal.Wait(TimeSpan.FromSeconds(6)))
        {
            Logger.Error("The live wallpaper surface did not attach in time; giving up.");
            Dispose();
            return false;
        }
        return Volatile.Read(ref _attachOk) == 1;
    }

    private void ThreadProc()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        // Every WPF DispatcherObject here (visual, player, timers, bitmaps) must be
        // created on THIS thread, not in the constructor, or rendering fails with
        // cross-thread exceptions.
        _visual = new DrawingVisual();
        var attached = false;
        try
        {
            _form = new WallpaperForm();
            var _ = _form.Handle; // create the native window without displaying it
            attached = DesktopLayer.Attach(_form.Handle) != DesktopAttachMode.Failed;
            if (!attached)
            {
                Logger.Error("The live wallpaper surface could not attach to the desktop layer.");
                return;
            }
            _graphics = System.Drawing.Graphics.FromHwnd(_form.Handle);
            // The surface background is always opaque black, so raw source-copy compositing
            // is visually identical to blending and skips the per-pixel alpha math.
            _graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            _graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            OpenMedia();
            StartPlayback();
            StartVisibilityMonitor();
            Volatile.Write(ref _attachOk, 1);
            // Signal the caller NOW — the finally-based signal only fires when this thread
            // exits, which would make Show() time out and kill a perfectly good surface.
            _attachSignal.Set();
            Logger.Info("Live wallpaper surface attached behind the desktop icons.");
            Dispatcher.Run(); // pumps the timers and the WinForms messages until shutdown
        }
        catch (Exception ex)
        {
            Logger.Error("The live wallpaper surface crashed.", ex);
        }
        finally
        {
            if (!attached)
            {
                Volatile.Write(ref _attachOk, 0);
            }
            _attachSignal.Set();
            DisposeCore();
            _exited.Set();
            if (Interlocked.Exchange(ref _onClosedInvoked, 1) == 0)
            {
                _onClosed?.Invoke();
            }
        }
    }

    // ============ media open ============

    private void OpenMedia()
    {
        if (WallpaperEngine.IsAnimatedGifFile(_mediaPath))
        {
            LoadGifFrames(_mediaPath);
        }
        else
        {
            OpenVideo(_mediaPath);
        }
    }

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
                    Logger.Error("Live video opened with an unknown size; closing the live surface.");
                    ShutdownSurface();
                    return;
                }
                Logger.Info($"Live video opened ({_videoW}x{_videoH}).");
                StartRenderLoop();
            };
            _player.MediaFailed += (_, e) =>
            {
                Logger.Error($"Live video failed to play: {e.ErrorException.Message}");
                ShutdownSurface();
            };
            _player.MediaEnded += (_, _) =>
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
            };
            _player.IsMuted = _muted;
            _player.Volume = _muted ? 0 : 1;
            // Required for offscreen rendering: without scrubbing the player never
            // exposes fresh frames to DrawVideo, so the surface shows one frozen frame.
            _player.ScrubbingEnabled = true;
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }
        catch (Exception ex)
        {
            Logger.Error("Opening the live video failed.", ex);
            ShutdownSurface();
        }
    }

    // ============ video render ============

    private void StartRenderLoop()
    {
        if (_renderTimer != null)
        {
            return;
        }
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _renderTimer.Tick += (_, _) => RenderVideoFrame();
        _renderTimer.Start();
        StartPlaybackProbe();
    }

    /// <summary>One-shot diagnostic: three samples to prove the player's clock advances.</summary>
    private void StartPlaybackProbe()
    {
        var probe = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        var samples = 0;
        probe.Tick += (_, _) =>
        {
            Logger.Info($"Playback probe: position {_player?.Position.TotalSeconds ?? 0:0.0}s.");
            if (++samples >= 3)
            {
                probe.Stop();
            }
        };
        probe.Start();
    }

    /// <summary>
    /// Polls a few times a second whether an app window covers the desktop. While covered
    /// the video is paused (decode stops) and frame blits are skipped, so foreground apps
    /// get the full CPU/GPU instead of fighting an invisible wallpaper. Resumes instantly
    /// when the desktop shows again.
    /// </summary>
    private void StartVisibilityMonitor()
    {
        if (_visibilityTimer != null)
        {
            return;
        }
        _desktopCovered = DesktopLayer.IsDesktopCovered();
        _visibilityTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _visibilityTimer.Tick += (_, _) => UpdateOcclusion();
        _visibilityTimer.Start();
        if (_desktopCovered)
        {
            UpdateOcclusion();
        }
    }

    private void UpdateOcclusion()
    {
        if (_closed)
        {
            return;
        }
        var covered = DesktopLayer.IsDesktopCovered();
        if (covered == _desktopCovered && covered)
        {
            return; // still covered — stay paused, no log spam
        }
        _desktopCovered = covered;
        var player = _player;
        try
        {
            if (covered)
            {
                if (player != null && !_playerPausedForCover)
                {
                    try { player.Pause(); } catch { }
                    _playerPausedForCover = true;
                }
            }
            else if (_playerPausedForCover)
            {
                _playerPausedForCover = false;
                if (player != null)
                {
                    try { player.Play(); } catch { }
                }
            }
        }
        catch
        {
            // Best effort — rendering skip below still applies.
        }
    }

    private void RenderVideoFrame()
    {
        // Invisible while an app covers the desktop — skip the GPU→CPU readback and
        // the full-screen GDI compose entirely instead of stuttering along unseen.
        if (_desktopCovered || _closed)
        {
            return;
        }
        // The blit can take longer than the 33 ms tick on slow GPUs / 4K frames — never
        // let ticks pile up on the dispatcher, just drop the late frame.
        if (Interlocked.Exchange(ref _rendering, 1) == 1)
        {
            return;
        }
        try
        {
            var player = _player;
        var visual = _visual;
        if (player == null || visual == null || _videoW <= 0 || _videoH <= 0 || _closed)
        {
            return;
        }
        try
        {
            // WinForms' ClientSize doesn't track the external SetWindowPos resize —
            // always read the true client rectangle.
            if (!NativeMethods.GetClientRect(_form.Handle, out var clientRect))
            {
                return;
            }
            var cw = clientRect.Right - clientRect.Left;
            var ch = clientRect.Bottom - clientRect.Top;
            if (!_renderLoopLogged)
            {
                _renderLoopLogged = true;
                Logger.Info($"Render loop running (client {cw}x{ch}, video {_videoW}x{_videoH}).");
            }
            if (cw <= 0 || ch <= 0)
            {
                return;
            }
            // Render at the video's own size, clamped to the window (aspect preserved)
            // instead of the full window: the GPU→CPU readback and the GDI bitmap then
            // scale with the video, not the desktop. The GDI draw performs the single
            // scale to the final destination rect.
            var scale = Math.Min(1.0, Math.Min((double)cw / _videoW, (double)ch / _videoH));
            var rw = Math.Max(1, (int)Math.Round(_videoW * scale));
            var rh = Math.Max(1, (int)Math.Round(_videoH * scale));
            if (_rtb == null || _rtb.PixelWidth != rw || _rtb.PixelHeight != rh)
            {
                _rtb = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
                _gdiFrame?.Dispose();
                _gdiFrame = new System.Drawing.Bitmap(rw, rh, GdiPixelFormat.Format32bppPArgb);
            }
            using (var ctx = visual.RenderOpen())
            {
                ctx.DrawRectangle(System.Windows.Media.Brushes.Black, null, new System.Windows.Rect(0, 0, rw, rh));
                ctx.DrawVideo(player, new System.Windows.Rect(0, 0, rw, rh));
            }
            _rtb.Render(visual);

            if (_gdiFrame == null)
            {
                return;
            }
            // Copy straight into the GDI bitmap's bits — no intermediate managed buffer.
            var stride = rw * 4;
            var data = _gdiFrame.LockBits(
                new System.Drawing.Rectangle(0, 0, rw, rh),
                ImageLockMode.WriteOnly, GdiPixelFormat.Format32bppPArgb);
            try
            {
                _rtb.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, stride * rh, stride);
            }
            finally
            {
                _gdiFrame.UnlockBits(data);
            }

            if (_graphics == null)
            {
                return;
            }
            PresentFrame(_gdiFrame, cw, ch, ComputeDestRect(cw, ch, rw, rh));
        }
        catch (Exception ex)
        {
            // The shell can destroy the wallpaper layer (e.g. on a wallpaper change),
            // which takes our window with it — fail gracefully.
            Logger.Error("Rendering the live video frame failed; closing the live surface.", ex);
            ShutdownSurface();
        }
        }
        finally
        {
            Volatile.Write(ref _rendering, 0);
        }
    }

    /// <summary>
    /// Composes the frame off-screen and presents it with a SINGLE blit. Drawing the
    /// clear and the scaled frame straight to the window would leave a half-painted
    /// all-black state that DWM regularly catches as a visible black flicker.
    /// </summary>
    private void PresentFrame(System.Drawing.Bitmap frame, int cw, int ch, System.Windows.Rect dest)
    {
        if (_presentBmp == null || _presentBmp.Width != cw || _presentBmp.Height != ch)
        {
            _presentGfx?.Dispose();
            _presentBmp?.Dispose();
            _presentBmp = new System.Drawing.Bitmap(cw, ch, GdiPixelFormat.Format32bppPArgb);
            _presentGfx = System.Drawing.Graphics.FromImage(_presentBmp);
            _presentGfx.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            _presentGfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
        }
        if (_presentGfx == null || _graphics == null)
        {
            return;
        }
        _presentGfx.Clear(System.Drawing.Color.Black);
        _presentGfx.DrawImage(
            frame,
            new System.Drawing.RectangleF((float)dest.X, (float)dest.Y, (float)dest.Width, (float)dest.Height));
        _graphics.DrawImageUnscaled(_presentBmp, 0, 0);
    }

    private System.Windows.Rect ComputeDestRect(int cw, int ch, int srcW, int srcH)
    {
        if (_fit == FitMode.Stretch)
        {
            return new System.Windows.Rect(0, 0, cw, ch);
        }
        if (_fit == FitMode.Fit)
        {
            var scale = Math.Min((double)cw / srcW, (double)ch / srcH);
            return Centered(scale);
        }
        // Fill (and everything else): cover the whole surface.
        var cover = Math.Max((double)cw / srcW, (double)ch / srcH);
        return Centered(cover);

        System.Windows.Rect Centered(double scale)
        {
            var w = Math.Max(1.0, srcW * scale);
            var h = Math.Max(1.0, srcH * scale);
            return new System.Windows.Rect((cw - w) / 2, (ch - h) / 2, w, h);
        }
    }

    // ============ animated GIF ============

    private void LoadGifFrames(string path)
    {
        try
        {
            // Keep the decoder open and decode frames ON DEMAND, one per tick into a
            // single reusable bitmap. Predecoding every frame at full size could easily
            // cost gigabytes (1080p × 300 frames ≈ 2.5 GB); this keeps resident memory
            // at roughly one frame.
            _gifDecoder = new GifBitmapDecoder(new Uri(path, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnDemand);
            _gifFrameCount = _gifDecoder.Frames.Count;
            _gifDelays = new TimeSpan[_gifFrameCount];
            for (var i = 0; i < _gifFrameCount; i++)
            {
                _gifDelays[i] = ReadFrameDelay(_gifDecoder.Frames[i]);
            }
            if (_gifFrameCount == 0)
            {
                Logger.Error($"Live GIF '{path}' decoded to zero frames; closing the live surface.");
                ShutdownSurface();
            }
            else
            {
                Logger.Info($"Live GIF loaded with {_gifFrameCount} frame(s) (decoded on demand).");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Decoding the live GIF '{path}' failed.", ex);
            ShutdownSurface();
        }
    }

    private static TimeSpan ReadFrameDelay(BitmapSource frame)
    {
        try
        {
            if (frame is BitmapFrame { Metadata: BitmapMetadata meta }
                && meta.GetQuery("/grctlext/Delay") is int centiseconds
                && centiseconds > 0)
            {
                return TimeSpan.FromMilliseconds(centiseconds * 10);
            }
        }
        catch
        {
            // Some GIFs have no (or odd) per-frame metadata.
        }
        return TimeSpan.FromMilliseconds(100);
    }

    private void StartPlayback()
    {
        if (_gifFrameCount > 0)
        {
            StartGifLoop();
        }
        // Video playback starts on MediaOpened.
    }

    private void StartGifLoop()
    {
        if (_gifTimer != null)
        {
            return;
        }
        _gifIndex = 0;
        Blit(_gifIndex);
        _gifTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = _gifDelays[0] };
        _gifTimer.Tick += (_, _) =>
        {
            _gifIndex = (_gifIndex + 1) % _gifFrameCount;
            Blit(_gifIndex);
            _gifTimer!.Interval = _gifDelays[_gifIndex];
        };
        _gifTimer.Start();
    }

    private void Blit(int frameIndex)
    {
        if (_desktopCovered || _closed)
        {
            return;
        }
        try
        {
            if (_graphics == null || _gifDecoder == null)
            {
                return;
            }
            var frame = _gifDecoder.Frames[frameIndex];
            if (!NativeMethods.GetClientRect(_form.Handle, out var clientRect))
            {
                return;
            }
            var cw = clientRect.Right - clientRect.Left;
            var ch = clientRect.Bottom - clientRect.Top;
            if (cw <= 0 || ch <= 0 || frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
            {
                return;
            }
            // Reuse one GDI bitmap across ticks — decode straight into its bits.
            if (_gdiFrame == null || _gdiFrame.Width != frame.PixelWidth || _gdiFrame.Height != frame.PixelHeight)
            {
                _gdiFrame?.Dispose();
                _gdiFrame = new System.Drawing.Bitmap(frame.PixelWidth, frame.PixelHeight, GdiPixelFormat.Format32bppPArgb);
            }
            var stride = frame.PixelWidth * 4;
            var data = _gdiFrame.LockBits(
                new System.Drawing.Rectangle(0, 0, frame.PixelWidth, frame.PixelHeight),
                ImageLockMode.WriteOnly, GdiPixelFormat.Format32bppPArgb);
            try
            {
                // GIF frames are palette/indexed — convert straight into the bitmap's
                // premultiplied BGRA bits (no intermediate managed buffer).
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                converted.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, stride * frame.PixelHeight, stride);
            }
            finally
            {
                _gdiFrame.UnlockBits(data);
            }
            var dest = ComputeDestRect(cw, ch, frame.PixelWidth, frame.PixelHeight);
            PresentFrame(_gdiFrame, cw, ch, dest);
        }
        catch (Exception ex)
        {
            Logger.Error("Rendering the live GIF frame failed; closing the live surface.", ex);
            ShutdownSurface();
        }
    }

    // ============ lifecycle ============

    /// <summary>Surface-thread self shutdown (decode failure, shell destroyed the layer).</summary>
    private void ShutdownSurface()
    {
        _closed = true;
        _renderTimer?.Stop();
        _renderTimer = null;
        _gifTimer?.Stop();
        _gifTimer = null;
        _visibilityTimer?.Stop();
        _visibilityTimer = null;
        try
        {
            _player?.Close();
        }
        catch
        {
        }
        _player = null;
        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    /// <summary>Full cleanup on the surface thread. Idempotent.</summary>
    private void DisposeCore()
    {
        _closed = true;
        _renderTimer?.Stop();
        _renderTimer = null;
        _gifTimer?.Stop();
        _gifTimer = null;
        _visibilityTimer?.Stop();
        _visibilityTimer = null;
        try
        {
            _player?.Close();
        }
        catch
        {
        }
        _player = null;
        _graphics?.Dispose();
        _graphics = null;
        _gifDecoder = null;
        _gifFrameCount = 0;
        _gifDelays = [];
        _gdiFrame?.Dispose();
        _gdiFrame = null;
        _presentGfx?.Dispose();
        _presentGfx = null;
        _presentBmp?.Dispose();
        _presentBmp = null;
        try
        {
            _form?.Dispose();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _closed = true;
        var dispatcher = _dispatcher;
        if (dispatcher == null)
        {
            // The thread never created its dispatcher (never really started) — finish
            // the lifecycle here so the owner learns the surface is gone.
            _exited.Set();
            if (Interlocked.Exchange(ref _onClosedInvoked, 1) == 0)
            {
                _onClosed?.Invoke();
            }
            return;
        }
        if (dispatcher.CheckAccess())
        {
            ShutdownSurface();
        }
        else
        {
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        }
        // Best-effort wait: dispatcher shutdown plus cleanup is normally instant.
        _thread?.Join(800);
    }
}
