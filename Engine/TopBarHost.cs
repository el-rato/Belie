using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using WallpaperProfiles.Workspaces;

namespace WallpaperProfiles.Engine;

/// <param name="ProfileId">The workspace's profile, or empty.</param>
/// <param name="Shortcut">The keys that opened it, when keys did.</param>
/// <param name="OnlyWithShortcut">Nothing is to be applied unless those keys are also a profile's.</param>
internal sealed record WorkspaceProfileRequest(Guid ProfileId, string Shortcut = "", bool OnlyWithShortcut = false);
internal sealed record WorkspaceCommand(int Workspace);

/// <summary>Owns the top bars and workspaces, either of which can be off. Runs outside the tray app, alongside the desktop widgets.</summary>
internal sealed class TopBarHost : IWorkspaceTransition, IDisposable
{
    private readonly TopBarStore _store;
    private readonly Action _exit;
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _stop, _changed, _command, _closed;
    private readonly RegisteredWaitHandle _stopWait, _changeWait, _commandWait;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _syncDelay = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly IWindowSystem _system;
    private readonly NativeTileSystem _tileSystem;
    private readonly Dictionary<string, SceneOverlay> _scenes = new();
    private TilingEngine? _tiling;
    private bool _dragging;
    private nint _dragged;
    private readonly System.Diagnostics.Stopwatch _dragTime = new();
    private readonly DispatcherTimer _titleDelay = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private WorkspaceProfileRequest? _waitingProfile;
    private bool _following, _reading;
    private long _lastSync;
    private readonly Dictionary<string, TopBarWindow> _windows = new();
    private readonly SystemReadings _readings = new();
    private readonly WindowsNowPlayingSource _media = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly WindowEvents _events;
    private readonly WorkspaceKeys _keys;
    private readonly BarScripts _scripts = new();
    private readonly FileSystemWatcher? _files;
    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _openingByKey;
    private WorkspaceManager? _workspaces;
    private TopBarSettings _settings;
    private NowPlayingState? _playing;
    private WallpaperColors? _colors;
    private string _colorsKey = "", _read = "";
    private bool _readingMedia;
    private int _ticks;
    private bool _disposed;

    internal WorkspaceManager? Workspaces => _workspaces;
    internal IReadOnlyCollection<TopBarWindow> Windows => _windows.Values;
    internal TilingEngine? Tiling => _tiling;
    internal SceneOverlay? Scene(string monitor) => _scenes.GetValueOrDefault(monitor);

    public TopBarHost(string file, Action exit, Func<DesktopWindow, bool>? only = null, Func<IProfileHotkeys>? hotkeys = null, bool guard = true)
    {
        _store = new TopBarStore(file);
        _exit = exit;
        _keys = new WorkspaceKeys(hotkeys ?? (() => new NativeProfileHotkeys()), OpenByKey, SendWindow,
            direction => WithTile((tiling, window) => { if (tiling.Neighbor(window, direction) is var next && next != 0) _system!.Focus(next); }),
            direction => WithTile((tiling, window) => tiling.Swap(window, direction)),
            () => WithTile((tiling, window) => tiling.ToggleFloating(window)), () => WithTile((tiling, window) => tiling.ToggleSplit(window)));
        _mutex = LocalIpc.Mutex(true, TopBarProcess.MutexName(file), out var first);
        if (!first) { _mutex.Dispose(); throw new InvalidOperationException("The top bar is already running."); }
        _stop = LocalIpc.Event(EventResetMode.AutoReset, TopBarProcess.MutexName(file) + ".Stop");
        _changed = LocalIpc.Event(EventResetMode.AutoReset, TopBarProcess.MutexName(file) + ".Changed");
        _command = LocalIpc.Event(EventResetMode.AutoReset, TopBarProcess.MutexName(file) + ".Command");
        // Reset in case the guard of an earlier bar still holds it open.
        _closed = LocalIpc.Event(EventResetMode.ManualReset, TopBarProcess.ClosedEventName(file));
        _closed.Reset();
        _settings = _store.Load();
        _tileSystem = new NativeTileSystem(() => _settings.FloatingApps);
        _system = new NativeWindowSystem(() => _settings.PinnedApps, () => _settings.PrimaryMonitorOnly, only, () => _settings.StickyPopouts);
        // Also covers a crash followed by turning workspaces off.
        new HiddenWindowJournal(HiddenWindowJournal.PathFor(_store.FilePath)).RestoreOrphans(_system);
        // The guard must be watching before the first window can be hidden.
        if (guard) TopBarProcess.StartGuard(file);
        _stopWait = Watch(_stop, _exit);
        _changeWait = Watch(_changed, Reload);
        _commandWait = Watch(_command, RunCommand);
        _titleDelay.Tick += (_, _) => { _titleDelay.Stop(); UpdateTitles(); };
        NativeMethods.KeepThreadFast();
        _syncDelay.Tick += (_, _) => { _syncDelay.Stop(); Sync(); };
        _events = new WindowEvents(WindowChanged);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _scripts.Read += (module, text, tip) => { foreach (var window in _windows.Values) window.UpdateScript(module, text, tip); };
        _reload.Tick += (_, _) => { _reload.Stop(); Reload(); };
        _files = WatchFiles();
        _dispatcher.BeginInvoke(Reload);
    }

    private RegisteredWaitHandle Watch(EventWaitHandle handle, Action action) =>
        ThreadPool.RegisterWaitForSingleObject(handle, (_, _) =>
        {
            if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(action);
        }, null, Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>Changes made with any editor show at once.</summary>
    private FileSystemWatcher? WatchFiles()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(_store.FilePath)!, Path.GetFileNameWithoutExtension(_store.FilePath) + ".*");
            void Seen(object sender, FileSystemEventArgs e)
            {
                if (e.FullPath.Equals(_store.FilePath, StringComparison.OrdinalIgnoreCase) || e.FullPath.Equals(_store.CssPath, StringComparison.OrdinalIgnoreCase))
                    if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => { if (!_disposed) { _reload.Stop(); _reload.Start(); } });
            }
            watcher.Changed += Seen; watcher.Created += Seen; watcher.Renamed += Seen; watcher.Deleted += Seen;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) { Logger.Warn("The bar's files cannot be watched: " + ex.Message); return null; }
    }

    private void WindowChanged(uint eventId, nint window)
    {
        if (_disposed) return;
        // Tiles wait while a window is being dragged; moving them would fight the pointer.
        if (eventId == WindowEvents.MoveStarted)
        {
            _dragging = true; _dragged = window;
            if (_tiling?.IsTiled(window) == true) { FinishScenes(); Follow(true); }
        }
        else if (eventId == WindowEvents.MoveEnded)
        {
            _dragging = false;
            Follow(false);
            _tiling?.UserMoved(window, System.Windows.Forms.Cursor.Position);
        }
        // Some apps animate their titles; a window that is already known has nothing else to say.
        if (eventId == 0x800C && (_workspaces == null || _workspaces.Knows(window)))
        {
            if (!_titleDelay.IsEnabled) _titleDelay.Start();
            return;
        }
        // A new or closed window is dealt with at once, or it would be seen out of place. Bursts still wait.
        if (eventId is 0x8001 or 0x8002 or 0x8003 && Environment.TickCount64 - _lastSync > 40) { _syncDelay.Stop(); Sync(); return; }
        if (!_syncDelay.IsEnabled) _syncDelay.Start();
    }

    /// <summary>Neighbors keep up with a dragged edge at the display's own rate.</summary>
    private void Follow(bool follow)
    {
        if (follow == _following) return;
        _following = follow;
        _dragTime.Restart();
        if (follow) System.Windows.Media.CompositionTarget.Rendering += FollowDrag; else System.Windows.Media.CompositionTarget.Rendering -= FollowDrag;
    }

    private void FollowDrag(object? sender, EventArgs e)
    {
        if (!_dragging) { Follow(false); return; }
        if (_dragTime.ElapsedMilliseconds < 6) return; // Raised more than once for some frames.
        _dragTime.Restart();
        _tiling?.UserResizing(_dragged);
    }

    private void RunCommand()
    {
        if (_disposed) return;
        try
        {
            if (AtomicJsonFile.Read<WorkspaceCommand>(TopBarProcess.CommandFile(_store.FilePath)) is { } command)
                _workspaces?.Switch(TargetMonitor(), command.Workspace);
        }
        catch (Exception ex) { Logger.Error("Opening a workspace failed.", ex); }
    }

    private bool Animates => _settings.AnimationsEnabled && System.Windows.SystemParameters.ClientAreaAnimation;

    private SceneOverlay SceneFor(string monitor)
    {
        if (!_scenes.TryGetValue(monitor, out var scene)) {
            _scenes[monitor] = scene = new SceneOverlay(_tileSystem, () => _settings.AnimationSpeed, () => _settings.Motion);
            // Tiles may have changed while the animation played.
            scene.Completed += () => { if (!_disposed && !_syncDelay.IsEnabled) _syncDelay.Start(); };
        }
        return scene;
    }

    TimeSpan IWorkspaceTransition.Duration => Animates ? TimeSpan.FromMilliseconds(Motion.SlideTime / _settings.AnimationSpeed + 400) : TimeSpan.Zero;

    void IWorkspaceTransition.Run(string monitor, int direction, IReadOnlyCollection<nint> leaving, IReadOnlyCollection<nint> arriving, Action show, Action hide)
    {
        if (!Animates || leaving.Count + arriving.Count == 0)
        {
            if (_scenes.TryGetValue(monitor, out var scene)) scene.Finish();
            show(); hide();
        }
        else SceneFor(monitor).Slide(monitor, direction, leaving, arriving, show, hide);
    }

    void IWorkspaceTransition.Finish() { foreach (var scene in _scenes.Values) scene.Finish(); }

    private void Retile()
    {
        // The end of a drag can be missed when its app closes.
        if (_dragging && System.Windows.Forms.Control.MouseButtons == System.Windows.Forms.MouseButtons.None) _dragging = false;
        if (_tiling == null || _workspaces == null || _dragging || _scenes.Values.Any(scene => scene.IsRunning)) return;
        var moves = _tiling.Update(_workspaces.Windows(), _workspaces.ActiveWindow);
        _tiling.Apply(moves.Where(move => move.Hidden || move.IsCorrection).ToArray());
        foreach (var group in moves.Where(move => !move.Hidden && !move.IsCorrection).GroupBy(move => move.Monitor))
        {
            var visible = group.ToArray();
            if (Animates) SceneFor(group.Key).Rearrange(group.Key, visible, () => _tiling?.Apply(visible));
            else _tiling.Apply(visible);
        }
    }

    private void WithTile(Action<TilingEngine, nint> action)
    {
        if (_tiling == null || _workspaces is not { ActiveWindow: not 0 } workspaces) return;
        action(_tiling, workspaces.ActiveWindow);
        Sync();
    }

    private void Reload()
    {
        if (_disposed) return;
        var settings = _store.Load();
        // Saving tells the bar twice, once by name and once by changing the file.
        var read = System.Text.Json.JsonSerializer.Serialize(settings, JsonOptions.Shared) + settings.Css;
        if (read == _read) return;
        _read = read;
        _settings = settings;
        if (!_settings.Runs) { _exit(); return; }
        if (!_settings.WorkspacesEnabled || !_settings.TilingEnabled) { FinishScenes(); _tiling?.Release(); _tiling = null; }
        if (!_settings.WorkspacesEnabled) { _workspaces?.Dispose(); _workspaces = null; }
        else if (_workspaces == null)
        {
            _workspaces = new WorkspaceManager(_system, new HiddenWindowJournal(HiddenWindowJournal.PathFor(_store.FilePath)), _settings.WorkspaceCount,
                transition: this);
            _workspaces.Changed += UpdateWorkspaces;
            _workspaces.Switched += RequestProfile;
        }
        else _workspaces.SetCount(_settings.WorkspaceCount);
        if (_settings.WorkspacesEnabled && _settings.TilingEnabled)
        {
            _tiling ??= new TilingEngine(_tileSystem);
            _tiling.Gap = _settings.TileGap; _tiling.Margin = _settings.TileMargin;
            _tiling.SmartGaps = _settings.SmartGaps; _tiling.SplitRatio = _settings.SplitRatio;
            _tiling.Layout = _settings.TileLayout; _tiling.TileFixedSize = _settings.TileFixedSize;
        }
        _keys.Apply(_settings);
        _colorsKey = "";
        foreach (var window in _windows.Values) window.Apply(_settings, _colors);
        Sync();
        Tick();
    }

    private void Sync()
    {
        if (_disposed) return;
        try
        {
            SendWaitingProfile();
            SyncMonitors();
            _workspaces?.Sync();
            Retile();
            UpdateTitles();
            _lastSync = Environment.TickCount64;
        }
        catch (Exception ex) { Logger.Error("Refreshing the top bar failed.", ex); }
    }

    private void SyncMonitors()
    {
        var screens = System.Windows.Forms.Screen.AllScreens.ToDictionary(screen => screen.DeviceName);
        var wanted = _settings.Enabled ? _system.Monitors().ToArray() : Array.Empty<string>();
        foreach (var gone in _windows.Keys.Where(name => !wanted.Contains(name)).ToArray())
        {
            _windows.Remove(gone, out var window);
            window!.Close();
        }
        foreach (var name in wanted)
        {
            if (!screens.TryGetValue(name, out var screen)) continue;
            if (_windows.TryGetValue(name, out var existing)) { existing.PlaceIfMoved(screen.Bounds); continue; }
            var window = new TopBarWindow(name);
            window.WorkspaceChosen += index => _workspaces?.Switch(name, index);
            window.WorkspaceScrolled += step => _workspaces?.Cycle(name, step);
            window.WindowSent += SendWindow;
            window.VolumeScrolled += step => { _readings.ChangeVolume(step); UpdateReadings(); };
            window.MuteClicked += () => { _readings.ToggleMute(); UpdateReadings(); };
            window.MediaCommand += SendMedia;
            window.LauncherClicked += Launch;
            window.SettingsClicked += OpenSettings;
            window.HideClicked += TurnOff;
            _windows.Add(name, window);
            window.Apply(_settings, _colors);
            window.Place(screen.Bounds);
            window.Show();
            if (_workspaces != null) window.UpdateWorkspaces(_workspaces.States(name));
        }
    }

    private void UpdateWorkspaces()
    {
        if (_workspaces == null) return;
        foreach (var (monitor, window) in _windows) window.UpdateWorkspaces(_workspaces.States(monitor));
    }

    private void UpdateTitles()
    {
        var foreground = _system.Foreground();
        var title = new StringBuilder(256);
        // The desktop and taskbar have window titles of their own that mean nothing here.
        if (NativeMethods.ClassName(foreground) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or ""))
            NativeMethods.GetWindowText(foreground, title, title.Capacity);
        var monitor = NativeWindowSystem.MonitorOf(foreground);
        foreach (var window in _windows.Values)
        {
            window.UpdateTitle(window.Monitor == monitor ? title.ToString() : "");
            // The shell's full-screen notice does not repeat when focus moves, so each bar looks again itself.
            window.CheckFullscreen();
        }
    }

    private void Tick()
    {
        if (_disposed) return;
        _keys.Retry();
        Sync();
        // Without bars there is nothing to read for them, and their scripts must not run.
        if (!_settings.Enabled) { _scripts.Tick(Array.Empty<BarModule>()); return; }
        foreach (var window in _windows.Values) window.UpdateClock(DateTime.Now);
        _scripts.Tick(_settings.Modules);
        RefreshColors();
        // An island shows changes as they happen, so it is told about them sooner.
        if (_ticks++ % 2 == 0 || _settings.Shows(BarModuleKind.Island)) { UpdateReadings(); RefreshMedia(); }
        if (_ticks % 5 == 1) RefreshWeather();
    }

    /// <summary>Asking for the network and the volume can take a while, and animations must not wait for it.</summary>
    private async void UpdateReadings()
    {
        if (_reading || _disposed) return;
        _reading = true;
        try
        {
            bool disk = _settings.Shows(BarModuleKind.Disk), network = _settings.Shows(BarModuleKind.Network);
            var reading = await Task.Run(() => _readings.Read(disk, network));
            if (!_disposed) foreach (var window in _windows.Values) window.UpdateSystem(reading);
        }
        catch (Exception ex) { Logger.Error("Reading the system for the top bar failed.", ex); }
        finally { _reading = false; }
    }

    private async void RefreshMedia()
    {
        if (_readingMedia || _disposed) return;
        _readingMedia = true;
        try
        {
            _playing = _settings.Shows(BarModuleKind.Media) || _settings.Shows(BarModuleKind.Island) && _settings.IslandShowsMusic ? await _media.ReadAsync(_cancel.Token) : null;
            if (!_disposed) foreach (var window in _windows.Values) window.UpdateMedia(_playing);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
        catch (Exception ex) { Logger.Error("Reading playback for the top bar failed.", ex); }
        finally { _readingMedia = false; }
    }

    /// <summary>Follows the wallpaper's colors when the settings ask for it.</summary>
    private async void RefreshColors()
    {
        if (_disposed || _settings.Colors != TopBarColors.Wallpaper) return;
        var path = WallpaperTransition.CurrentWallpaper().Path;
        var key = File.Exists(path) ? path + "|" + File.GetLastWriteTimeUtc(path).Ticks : "";
        if (key.Length == 0 || key == _colorsKey) return;
        _colorsKey = key;
        try
        {
            var colors = await Task.Run(() => WallpaperColors.Read(path));
            if (_disposed || colors == null || key != _colorsKey) return;
            if (_colors == colors) return;
            _colors = colors;
            foreach (var window in _windows.Values) window.Apply(_settings, _colors);
        }
        catch (Exception ex) { Logger.Warn("The wallpaper's colors could not be read: " + ex.Message); }
    }

    private async void RefreshWeather()
    {
        try
        {
            var report = _settings.Shows(BarModuleKind.Weather) ? await WeatherSource.GetAsync(_settings.WeatherPlace, _settings.WeatherFahrenheit) : null;
            if (!_disposed) foreach (var window in _windows.Values) window.UpdateWeather(report);
        }
        catch (Exception ex) { Logger.Error("Showing the weather failed.", ex); }
    }

    private void Launch(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        try
        {
            switch (command.ToLowerInvariant())
            {
                case "belie:wallpapers": RequestWallpaper(WallpaperAction.Switcher); return;
                case "belie:next-wallpaper": RequestWallpaper(WallpaperAction.Next); return;
                case "belie:previous-wallpaper": RequestWallpaper(WallpaperAction.Previous); return;
                case "belie:settings": OpenSettings(); return;
                case "belie:mute": _readings.ToggleMute(); UpdateReadings(); return;
                case "belie:volume-up": _readings.ChangeVolume(2); UpdateReadings(); return;
                case "belie:volume-down": _readings.ChangeVolume(-2); UpdateReadings(); return;
                case "belie:play": SendMedia(PlaybackCommand.Toggle); return;
                case "belie:next-track": SendMedia(PlaybackCommand.Next); return;
                case "belie:previous-track": SendMedia(PlaybackCommand.Previous); return;
                case "belie:next-workspace": _workspaces?.Cycle(TargetMonitor(), 1); return;
                case "belie:previous-workspace": _workspaces?.Cycle(TargetMonitor(), -1); return;
            }
            // An unrecognized "belie:" string is not a shell command and is never handed to the shell.
            if (BarActions.ClaimsBuiltIn(command)) { Logger.Warn("Ignoring an unknown bar action: " + command); return; }
            var (file, arguments) = SplitCommand(command);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, arguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Logger.Warn("The bar could not open its launcher: " + ex.Message); }
    }

    /// <summary>The app and what it is given. An address or the path of a file that exists is taken whole.</summary>
    internal static (string File, string Arguments) SplitCommand(string command)
    {
        if (command.StartsWith('"') && command.IndexOf('"', 1) is var end and > 0) return (command[1..end], command[(end + 1)..].Trim());
        var space = command.IndexOf(' ');
        if (space < 0 || command.Contains("://") || File.Exists(command) || Directory.Exists(command)) return (command, "");
        // A path may have spaces in it before its arguments begin.
        for (var next = space; next > 0; next = command.IndexOf(' ', next + 1))
            if (Path.IsPathRooted(command) && File.Exists(command[..next])) return (command[..next], command[(next + 1)..].Trim());
        return (command[..space], command[(space + 1)..].Trim());
    }

    private static void RequestWallpaper(WallpaperAction action)
    {
        if (!EventWaitHandle.TryOpenExisting(TopBarProcess.WallpaperEventName(action), out var request))
        {
            // The switcher belongs to the tray app.
            if (action == WallpaperAction.Switcher) DetachedProcess.Start("--minimized");
            return;
        }
        using (request)
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            request.Set();
        }
    }

    private async void SendMedia(PlaybackCommand command)
    {
        try { await _media.SendAsync(command, TimeSpan.Zero, _cancel.Token); RefreshMedia(); }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
        catch (Exception ex) { Logger.Error("Changing playback from the top bar failed.", ex); }
    }

    /// <summary>A workspace's keys may also be a profile's, so the tray app is told which ones were pressed.</summary>
    private void OpenByKey(int index, KeyGesture keys)
    {
        if (_workspaces == null) return;
        var monitor = TargetMonitor();
        _openingByKey = true;
        bool switched;
        try { switched = _workspaces.Switch(monitor, index); }
        finally { _openingByKey = false; }
        SendProfileWhenStill(new WorkspaceProfileRequest(_settings.WorkspaceProfiles.GetValueOrDefault(index), ProfileShortcutManager.Format(keys),
            OnlyWithShortcut: !switched || _system.Monitors().FirstOrDefault() != monitor));
    }

    /// <summary>Shortcuts act where you are working: the focused window's monitor, or the pointer's.</summary>
    private string TargetMonitor() => _workspaces?.ActiveMonitor
        ?? System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).DeviceName;

    private void SendWindow(int index) { if (_workspaces is { ActiveWindow: not 0 } workspaces) workspaces.Move(workspaces.ActiveWindow, index); }

    private void RequestProfile(string monitor, int index)
    {
        // A profile covers every monitor, so only one monitor may choose it.
        if (_openingByKey || _system.Monitors().FirstOrDefault() != monitor || !_settings.WorkspaceProfiles.TryGetValue(index, out var profile)) return;
        SendProfileWhenStill(new WorkspaceProfileRequest(profile));
    }

    /// <summary>A new wallpaper makes Explorer and many apps redraw, so it waits until the windows have stopped moving.</summary>
    private void SendProfileWhenStill(WorkspaceProfileRequest wanted)
    {
        if (!_scenes.Values.Any(scene => scene.IsRunning)) { _waitingProfile = null; SendProfile(wanted); return; }
        // The same keys pressed again ask for nothing more than what is already waiting.
        if (wanted.OnlyWithShortcut && _waitingProfile is { OnlyWithShortcut: false } waiting && waiting.Shortcut == wanted.Shortcut) return;
        _waitingProfile = wanted;
    }

    private void SendWaitingProfile()
    {
        if (_waitingProfile is not { } wanted || _scenes.Values.Any(scene => scene.IsRunning)) return;
        _waitingProfile = null;
        SendProfile(wanted);
    }

    private void SendProfile(WorkspaceProfileRequest wanted)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(TopBarProcess.ProfileEventName, out var request)) return;
            using (request)
            {
                AtomicJsonFile.Write(TopBarProcess.ProfileRequestFile(_store.FilePath), wanted);
                request.Set();
            }
        }
        catch (Exception ex) { Logger.Error("Applying a workspace profile failed.", ex); }
    }

    private static void OpenSettings()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(TopBarProcess.SettingsEventName, out var open)) { using (open) open.Set(); }
            else DetachedProcess.Start("--topbar-settings");
        }
        catch (Exception ex) { Logger.Error("Opening top bar settings failed.", ex); }
    }

    private void TurnOff()
    {
        try { var settings = _store.Load(); settings.Enabled = false; _store.Save(settings); }
        catch (Exception ex) { Logger.Error("Turning off the top bar failed.", ex); }
    }

    private void FinishScenes() { foreach (var scene in _scenes.Values) scene.Finish(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _syncDelay.Stop(); _titleDelay.Stop(); Follow(false);
        // Bring windows back before anything else can fail.
        FinishScenes();
        _workspaces?.Dispose();
        _tiling?.Release();
        foreach (var scene in _scenes.Values) scene.Dispose();
        _events.Dispose();
        _keys.Dispose();
        _scripts.Dispose(); _files?.Dispose(); _reload.Stop();
        _stopWait.Unregister(null); _changeWait.Unregister(null); _commandWait.Unregister(null);
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear();
        _cancel.Cancel();
        _media.Dispose(); _readings.Dispose();
        _stop.Dispose(); _changed.Dispose(); _command.Dispose();
        _mutex.ReleaseMutex(); _mutex.Dispose();
        _closed.Set(); _closed.Dispose();
    }
}
