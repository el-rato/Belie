using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Engine;

internal enum WallpaperAction { Switcher, Next, Previous }

/// <summary>The keys that open the wallpaper switcher and step through the active profile's wallpapers.</summary>
internal sealed class WallpaperKeys : IDisposable
{
    private readonly Func<IProfileHotkeys> _create;
    private readonly Action<WallpaperAction> _run;
    private readonly Dictionary<WallpaperAction, string> _problems = new();
    private IProfileHotkeys? _hotkeys;
    private (string, string, string) _applied;

    public WallpaperKeys(Action<WallpaperAction> run, Func<IProfileHotkeys>? create = null)
    {
        _run = run;
        _create = create ?? (() => new NativeProfileHotkeys());
    }

    /// <summary>Why some keys do not work, or empty.</summary>
    public string Problem => string.Join(" ", _problems.Values);

    public void Apply(AppSettings settings)
    {
        var wanted = (settings.SwitcherKeys, settings.NextWallpaperKeys, settings.PreviousWallpaperKeys);
        // Keys another app held may have become free.
        if (wanted == _applied && _problems.Count == 0) return;
        Dispose();
        _applied = wanted;
        foreach (var (action, text) in new[] { (WallpaperAction.Switcher, wanted.Item1), (WallpaperAction.Next, wanted.Item2), (WallpaperAction.Previous, wanted.Item3) })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!ProfileShortcutManager.TryParse(text, out var gesture, out var error)) { _problems[action] = $"{text}: {error}"; continue; }
            if (_hotkeys == null)
            {
                _hotkeys = _create();
                _hotkeys.Pressed += id => _run((WallpaperAction)(id - 1));
            }
            if (!_hotkeys.Register((int)action + 1, gesture!)) _problems[action] = $"{text} is used by something else.";
        }
        if (_problems.Count > 0) Logger.Warn("Wallpaper shortcuts: " + Problem);
    }

    public void Dispose()
    {
        _hotkeys?.Dispose(); _hotkeys = null;
        _problems.Clear();
        _applied = default;
    }
}
