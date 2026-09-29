using System.Windows;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

/// <summary>
/// Owns the icons-friendly live wallpaper player (video pushed into the real wallpaper).
/// All public methods are safe to call from any thread.
/// </summary>
internal sealed class IconSafeLiveController : IDisposable
{
    private IconSafeLivePlayer? _player;
    private string _path = "";

    public bool IsActive => _player != null;

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
            Logger.Warn($"Icons-friendly live wallpaper file not found: '{path}'.");
            Stop();
            return;
        }
        RunOnUi(() =>
        {
            if (_player != null && string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
            {
                // Already running this media — just adopt the new generation/fit so the
                // frame pushes don't get skipped as superseded by the re-activation.
                _player.Refresh(profile.FitMode);
                return;
            }
            Stop();
            _player = new IconSafeLivePlayer();
            _path = path;
            _player.Start(path, profile.FitMode);
        });
    }

    public void Stop()
    {
        if (_player == null)
        {
            return;
        }
        RunOnUi(() =>
        {
            _player?.Dispose();
            _player = null;
            _path = "";
        });
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.InvokeAsync(action);
        }
    }

    public void Dispose() => Stop();
}
