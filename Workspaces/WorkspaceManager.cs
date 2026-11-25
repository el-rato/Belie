using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Workspaces;

internal readonly record struct WorkspaceState(int Index, bool IsActive, int WindowCount);
internal readonly record struct WorkspaceWindow(nint Handle, string Monitor, int Index, bool Hidden);

/// <summary>Carries out a switch. Both actions must run, in order, even if the switch is cut short by another.</summary>
internal interface IWorkspaceTransition
{
    TimeSpan Duration { get; }
    void Run(string monitor, int direction, IReadOnlyCollection<nint> leaving, IReadOnlyCollection<nint> arriving, Action show, Action hide);
    void Finish();
}

/// <summary>
/// Belie's own workspaces: every monitor has its own numbered set, and switching hides one group of windows and
/// shows another. One rule keeps this predictable: a visible window always belongs to the open workspace of the
/// monitor it is on. Windows that show themselves, or that Windows refuses to hide, simply join that workspace.
/// Use from one thread.
/// </summary>
internal sealed class WorkspaceManager : IDisposable
{
    // Showing and hiding are asynchronous so a hung app cannot freeze the bar. Until this passes, what
    // Windows reports about a window may predate our request.
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(750);

    private sealed class Entry
    {
        public required DesktopWindow Window;
        public required string Monitor;
        public int Index;
        public bool Hidden;
        public DateTime SettlesAt;
    }

    private readonly IWindowSystem _system;
    private readonly HiddenWindowJournal _journal;
    private readonly Func<DateTime> _now;
    private readonly IWorkspaceTransition? _transition;
    private readonly Dictionary<nint, Entry> _entries = new();
    private readonly Dictionary<string, int> _active = new();
    private readonly Dictionary<(string Monitor, int Index), nint> _lastFocused = new();
    private nint _lastForeground;
    private bool _disposed;

    public int Count { get; private set; }
    public event Action? Changed;
    public event Action<string, int>? Switched;

    public WorkspaceManager(IWindowSystem system, HiddenWindowJournal journal, int count, Func<DateTime>? now = null,
        IWorkspaceTransition? transition = null)
    {
        _transition = transition;
        _system = system; _journal = journal; _now = now ?? (() => DateTime.UtcNow);
        Count = Math.Max(1, count);
        _journal.RestoreOrphans(system);
    }

    public int Active(string monitor) => _active.GetValueOrDefault(monitor, 1);

    public IReadOnlyList<WorkspaceState> States(string monitor) => Enumerable.Range(1, Count)
        .Select(index => new WorkspaceState(index, index == Active(monitor),
            _entries.Values.Count(entry => entry.Monitor == monitor && entry.Index == index && entry.Window.Root == entry.Window.Handle))).ToArray();

    /// <summary>App windows on every workspace, without the dialogs that follow them.</summary>
    public IReadOnlyList<WorkspaceWindow> Windows() => _entries.Values.Where(entry => entry.Window.Root == entry.Window.Handle)
        .Select(entry => new WorkspaceWindow(entry.Window.Handle, entry.Monitor, entry.Index, entry.Hidden)).ToArray();

    public bool Knows(nint window) => _entries.ContainsKey(window);

    /// <summary>The window shortcuts act on: the focused app window, or the last one if the bar has focus.</summary>
    public nint ActiveWindow => _entries.TryGetValue(_lastForeground, out var entry) && !entry.Hidden ? _lastForeground : 0;
    public string? ActiveMonitor => ActiveWindow == 0 ? null : _entries[ActiveWindow].Monitor;

    /// <summary>Brings the records in line with what is on screen.</summary>
    public void Sync()
    {
        if (_disposed) return;
        var monitors = _system.Monitors();
        if (monitors.Count == 0) return; // The display is briefly gone, for example while waking up.
        AdoptMonitors(monitors);
        var now = _now();
        var hiddenChanged = false;
        var visible = _system.VisibleWindows().Where(window => monitors.Contains(window.Monitor)).ToDictionary(window => window.Handle);
        // Owners first, so dialogs can follow them.
        foreach (var window in visible.Values.OrderBy(window => window.Root != window.Handle))
        {
            if (_entries.TryGetValue(window.Handle, out var entry) && entry.Window.Identity != window.Identity)
            {
                hiddenChanged |= entry.Hidden;
                _entries.Remove(window.Handle);
                entry = null;
            }
            if (entry?.Hidden == true && now < entry.SettlesAt) continue;
            if (entry == null) _entries.Add(window.Handle, entry = new Entry { Window = window, Monitor = window.Monitor });
            hiddenChanged |= entry.Hidden;
            entry.Hidden = false;
            entry.Window = window;
            entry.Monitor = window.Root != window.Handle && _entries.TryGetValue(window.Root, out var owner) ? owner.Monitor : window.Monitor;
            entry.Index = Active(entry.Monitor);
        }
        foreach (var (handle, entry) in _entries.ToArray())
        {
            if (visible.ContainsKey(handle)) continue;
            if (entry.Hidden ? _system.Exists(entry.Window.Identity) : now < entry.SettlesAt) continue;
            hiddenChanged |= entry.Hidden;
            _entries.Remove(handle);
        }
        var foreground = _system.Foreground();
        if (_entries.TryGetValue(foreground, out var focused) && !focused.Hidden) _lastForeground = foreground;
        if (hiddenChanged) SaveJournal(HiddenWindows());
        Changed?.Invoke();
    }

    private void AdoptMonitors(IReadOnlyList<string> monitors)
    {
        foreach (var gone in _active.Keys.Where(monitor => !monitors.Contains(monitor)).ToArray())
        {
            var home = monitors[0];
            foreach (var entry in _entries.Values.Where(entry => entry.Monitor == gone))
            {
                entry.Monitor = home;
                // Visible windows are placed by Sync; only hidden ones need a decision here.
                if (entry.Hidden && entry.Index == Active(home)) ShowEntry(entry);
            }
            _active.Remove(gone);
        }
        foreach (var monitor in monitors) _active.TryAdd(monitor, 1);
    }

    public bool Switch(string monitor, int index)
    {
        if (_disposed || index < 1 || index > Count) return false;
        Sync();
        if (!_active.TryGetValue(monitor, out var previous) || previous == index) return false;
        var hide = _entries.Values.Where(entry => entry.Monitor == monitor && entry.Index == previous && !entry.Hidden).ToArray();
        var show = _entries.Values.Where(entry => entry.Monitor == monitor && entry.Index == index && entry.Hidden).ToArray();
        // Nothing is hidden unless it can be found again after a crash.
        if (!SaveJournal(HiddenWindows().Concat(hide.Select(entry => entry.Window.Identity)))) return false;
        if (hide.Any(entry => entry.Window.Handle == _lastForeground)) _lastFocused[(monitor, previous)] = _lastForeground;
        _active[monitor] = index;
        // Show, focus, then hide, so the desktop does not flash and Windows does not pick its own next window.
        var duration = _transition?.Duration ?? TimeSpan.Zero;
        var settles = _now() + Settle + duration;
        foreach (var entry in show) { entry.Hidden = false; entry.SettlesAt = settles; }
        foreach (var entry in hide) { entry.Hidden = true; entry.SettlesAt = settles; }
        var focus = show.FirstOrDefault(entry => _lastFocused.TryGetValue((monitor, index), out var last) && entry.Window.Handle == last)
            ?? show.FirstOrDefault(entry => entry.Window.Root == entry.Window.Handle);
        _lastForeground = focus?.Window.Handle ?? (hide.Length > 0 ? 0 : _lastForeground);
        // Anything may have changed by the time an animation gets here, so each step rechecks its windows.
        void Show()
        {
            foreach (var entry in show.Where(entry => !entry.Hidden)) _system.Show(entry.Window.Handle);
            if (focus is { Hidden: false }) _system.Focus(focus.Window.Handle);
            else if (hide.Length > 0 && focus == null) _system.FocusDesktop();
        }
        void Hide()
        {
            if (_disposed) return;
            foreach (var entry in hide.Where(entry => entry.Hidden && _entries.GetValueOrDefault(entry.Window.Handle) == entry)) _system.Hide(entry.Window.Handle);
            SaveJournal(HiddenWindows());
        }
        if (_transition == null) { Show(); Hide(); }
        else _transition.Run(monitor, Math.Sign(index - previous), hide.Select(entry => entry.Window.Handle).ToArray(),
            show.Select(entry => entry.Window.Handle).ToArray(), Show, Hide);
        Changed?.Invoke();
        Switched?.Invoke(monitor, index);
        return true;
    }

    public bool Cycle(string monitor, int step) => Switch(monitor, ((Active(monitor) - 1 + step) % Count + Count) % Count + 1);

    /// <summary>Sends a window and its dialogs to another workspace on the same monitor.</summary>
    public bool Move(nint window, int index)
    {
        if (_disposed || index < 1 || index > Count) return false;
        _transition?.Finish();
        Sync();
        if (!_entries.TryGetValue(window, out var target) || target.Hidden || target.Index == index) return false;
        var root = target.Window.Root;
        var group = _entries.Values.Where(entry => !entry.Hidden && entry.Monitor == target.Monitor
            && (entry.Window.Root == root || entry.Window.Handle == root)).ToArray();
        if (!SaveJournal(HiddenWindows().Concat(group.Select(entry => entry.Window.Identity)))) return false;
        _lastFocused[(target.Monitor, index)] = window;
        foreach (var entry in group) { entry.Index = index; HideEntry(entry); }
        Changed?.Invoke();
        return true;
    }

    public void SetCount(int count)
    {
        count = Math.Max(1, count);
        if (_disposed || count == Count) return;
        _transition?.Finish();
        Count = count;
        foreach (var monitor in _active.Keys.ToArray()) _active[monitor] = Math.Min(_active[monitor], count);
        foreach (var entry in _entries.Values)
        {
            entry.Index = Math.Min(entry.Index, count);
            if (entry.Hidden && entry.Index == Active(entry.Monitor)) ShowEntry(entry);
        }
        SaveJournal(HiddenWindows());
        Changed?.Invoke();
    }

    /// <summary>Puts every window back on screen.</summary>
    public void ShowAll()
    {
        _transition?.Finish();
        foreach (var entry in _entries.Values)
        {
            if (entry.Hidden && _system.Exists(entry.Window.Identity)) ShowEntry(entry);
            entry.Index = Active(entry.Monitor);
        }
        SaveJournal(Array.Empty<WindowIdentity>());
    }

    private void ShowEntry(Entry entry)
    {
        entry.Hidden = false; entry.SettlesAt = _now() + Settle;
        _system.Show(entry.Window.Handle);
    }

    private void HideEntry(Entry entry)
    {
        entry.Hidden = true; entry.SettlesAt = _now() + Settle;
        _system.Hide(entry.Window.Handle);
    }

    private IEnumerable<WindowIdentity> HiddenWindows() => _entries.Values.Where(entry => entry.Hidden).Select(entry => entry.Window.Identity);

    private bool SaveJournal(IEnumerable<WindowIdentity> windows)
    {
        try { _journal.Save(windows.Distinct().ToArray()); return true; }
        catch (Exception ex) { Logger.Error("Recording hidden windows failed; the workspace was left as it is.", ex); return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        ShowAll();
        _disposed = true;
        _entries.Clear();
    }
}
