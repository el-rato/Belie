using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

/// <summary>
/// Owns the single live-wallpaper surface. All public methods are safe to call from any
/// thread; the heavy work (attach + render) happens on the surface's own thread. The
/// controller is intentionally dumb about resolution logic — the coordinator decides
/// when a live profile activates and calls <see cref="Start"/>/<see cref="Stop"/>.
/// </summary>
internal sealed class LiveWallpaperController : IDisposable
{
    public sealed record LiveSurfaceInfo(string MediaPath, FitMode Fit, bool Muted);

    private readonly object _gate = new();
    private LiveWallpaperSurface? _surface;
    private string _path = "";
    private FitMode _fit = FitMode.Fill;
    private bool _muted = true;

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _surface != null;
            }
        }
    }

    /// <summary>Shows <paramref name="profile"/>'s video/GIF behind the desktop icons.</summary>
    public void Start(WallpaperProfile profile)
    {
        var path = profile.FolderPath;
        if (!WallpaperEngine.IsLiveFile(path))
        {
            Stop();
            return;
        }
        if (!File.Exists(path))
        {
            Logger.Warn($"Live wallpaper file not found: '{path}'. The previous static wallpaper stays in place.");
            Stop();
            return;
        }
        var fit = profile.FitMode;
        var muted = profile.VideoMuted;
        _ = Task.Run(() => StartCore(path, fit, muted));
    }

    /// <summary>Convenience wrapper for command-line use.</summary>
    public void StartVideo(string path, FitMode fitMode, bool muted)
        => _ = Task.Run(() => StartCore(path, fitMode, muted));

    public void Stop()
    {
        _ = Task.Run(() =>
        {
            lock (_gate)
            {
                if (_surface == null && _path.Length == 0)
                {
                    return;
                }
                _surface?.Dispose();
                _surface = null;
                _path = "";
            }
        });
    }

    /// <summary>
    /// Snapshot of the media currently playing on the smooth overlay (null when nothing
    /// is playing). Used to hand the live wallpaper over to the detached host on exit.
    /// </summary>
    public LiveSurfaceInfo? CaptureInfo()
    {
        lock (_gate)
        {
            return _surface == null ? null : new LiveSurfaceInfo(_path, _fit, _muted);
        }
    }

    private void StartCore(string path, FitMode fitMode, bool muted)
    {
        lock (_gate)
        {
            // Already playing this exact media with the same presentation — don't restart
            // (Activate can be invoked repeatedly for the same profile, e.g. via force-reapply).
            if (_surface != null
                && string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)
                && _fit == fitMode
                && _muted == muted)
            {
                return;
            }
            _surface?.Dispose();
            _surface = null;
            LiveWallpaperSurface? surface = null;
            surface = new LiveWallpaperSurface(path, fitMode, muted, onClosed: () =>
            {
                // The surface can close itself (decode failure, shell destroyed the layer) —
                // forget it so IsActive stays truthful. Runs on the surface thread; never
                // take the gate here (StartCore holds it while Show() waits for the attach).
                _ = Task.Run(() =>
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_surface, surface))
                        {
                            _surface = null;
                            _path = "";
                        }
                    }
                });
            });
            if (!surface.Show())
            {
                return;
            }
            _surface = surface;
            _path = path;
            _fit = fitMode;
            _muted = muted;
        }
    }

    public void Dispose() => Stop();
}
