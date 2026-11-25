using System.Windows.Input;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Workspaces;

namespace WallpaperProfiles.Engine;

/// <summary>
/// The keys of workspaces and tiles. Keys something else holds are asked for again until they are free, because the
/// tray app lets go of the profile shortcuts that are workspace keys once it sees the bar running.
/// </summary>
internal sealed class WorkspaceKeys : IDisposable
{
    private const ModifierKeys ControlAlt = ModifierKeys.Control | ModifierKeys.Alt;
    /// <summary>How long keys may stay taken before the other combination is offered instead.</summary>
    internal const int Patience = 6000;

    private sealed record Wanted(int Id, KeyGesture Keys, Action<KeyGesture> Run, KeyGesture? Other = null) { public int OtherId => Id + 500; }

    private readonly Func<IProfileHotkeys> _create;
    private readonly Action<int, KeyGesture> _open;
    private readonly Action<int> _send;
    private readonly Func<long> _now;
    private readonly IReadOnlyList<(KeyGesture Keys, Action Run)> _tiling;
    private readonly List<Wanted> _wanted = new();
    private readonly Dictionary<int, Action> _held = new();
    private IProfileHotkeys? _hotkeys;
    private (WorkspaceShortcutModifier Modifier, int Count, bool Tiling) _applied;
    private long _since;
    private bool _warned;

    public WorkspaceKeys(Func<IProfileHotkeys> create, Action<int, KeyGesture> open, Action<int> send,
        Action<TileDirection> focus, Action<TileDirection> swap, Action toggleFloating, Action toggleSplit, Func<long>? now = null)
    {
        _create = create; _open = open; _send = send;
        _now = now ?? (() => Environment.TickCount64);
        var tiling = new List<(KeyGesture, Action)>();
        foreach (var (direction, key) in new[] { (TileDirection.Left, Key.Left), (TileDirection.Right, Key.Right), (TileDirection.Up, Key.Up), (TileDirection.Down, Key.Down) })
        {
            tiling.Add((new KeyGesture(key, ControlAlt), () => focus(direction)));
            tiling.Add((new KeyGesture(key, ControlAlt | ModifierKeys.Shift), () => swap(direction)));
        }
        tiling.Add((new KeyGesture(Key.F, ControlAlt), toggleFloating));
        tiling.Add((new KeyGesture(Key.J, ControlAlt), toggleSplit));
        _tiling = tiling;
    }

    public void Apply(TopBarSettings settings)
    {
        var wanted = (settings.WorkspacesEnabled ? settings.Shortcuts : WorkspaceShortcutModifier.None, settings.WorkspaceCount, settings.TilingEnabled);
        if (wanted == _applied) return;
        _applied = wanted;
        Dispose();
        if (_applied.Modifier == WorkspaceShortcutModifier.None) return;
        _hotkeys = _create();
        _hotkeys.Pressed += id => { if (_held.TryGetValue(id, out var action)) action(); };
        var (keys, other) = _applied.Modifier == WorkspaceShortcutModifier.Alt ? (ModifierKeys.Alt, ControlAlt) : (ControlAlt, ModifierKeys.Alt);
        for (var index = 1; index <= _applied.Count; index++)
        {
            var target = index;
            _wanted.Add(new Wanted(index, new KeyGesture(Key.D0 + index, keys), gesture => _open(target, gesture), new KeyGesture(Key.D0 + index, other)));
            _wanted.Add(new Wanted(100 + index, new KeyGesture(Key.D0 + index, keys | ModifierKeys.Shift), _ => _send(target),
                new KeyGesture(Key.D0 + index, other | ModifierKeys.Shift)));
        }
        if (_applied.Tiling)
            for (var i = 0; i < _tiling.Count; i++)
            {
                var (gesture, run) = _tiling[i];
                _wanted.Add(new Wanted(200 + i, gesture, _ => run()));
            }
        _since = _now();
        _warned = false;
        Retry();
    }

    /// <summary>Asks again for the keys that were taken. Cheap when there are none.</summary>
    public void Retry()
    {
        if (_hotkeys == null) return;
        var waited = _now() - _since >= Patience;
        var missing = _wanted.Where(item => !_held.ContainsKey(item.Id)).ToArray();
        foreach (var item in missing)
        {
            if (Hold(item.Id, item.Keys, item.Run))
            {
                if (_held.Remove(item.OtherId)) _hotkeys.Unregister(item.OtherId);
            }
            // The other combination reaches the workspace as long as the keys belong to something else.
            else if (waited && item.Other != null && !_held.ContainsKey(item.OtherId)) Hold(item.OtherId, item.Other, item.Run);
        }
        if (!waited || _warned) return;
        _warned = true;
        var taken = missing.Where(item => !_held.ContainsKey(item.Id)).Select(item => ProfileShortcutManager.Format(item.Keys)).ToArray();
        if (taken.Length > 0) Logger.Warn("Workspace shortcuts used by another app: " + string.Join(", ", taken));
    }

    private bool Hold(int id, KeyGesture keys, Action<KeyGesture> run)
    {
        if (!_hotkeys!.Register(id, keys)) return false;
        _held[id] = () => run(keys);
        return true;
    }

    public void Dispose()
    {
        _hotkeys?.Dispose(); _hotkeys = null;
        _held.Clear(); _wanted.Clear();
    }
}
