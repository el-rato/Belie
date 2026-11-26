using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;

namespace WallpaperProfiles;

internal partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\Belie.SingleInstance";
    private const string ActivateEventName = @"Local\Belie.Activate";

    private Mutex? _mutex;
    private WallpaperCoordinator? _coordinator;
    private TrayIconManager? _tray;
    private MainWindow? _main;
    private ProfileShortcutManager? _profileShortcuts;
    private WallpaperKeys? _wallpaperKeys;
    private WallpaperSwitcher? _switcher;
    private EventWaitHandle[] _wallpaperEvents = Array.Empty<EventWaitHandle>();
    private readonly System.Windows.Threading.DispatcherTimer _barTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private TopBarSettings? _barSettings;
    private (bool Running, DateTime Saved) _barState;
    private Func<MainWindow>? _createMain;
    private DesktopHosts? _desktopHosts;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _canvasActivateEvent;
    private EventWaitHandle? _exitEvent;
    private EventWaitHandle? _barSettingsEvent;
    private EventWaitHandle? _barProfileEvent;
    // Long enough that reopening Belie soon after closing it finds everything as it was.
    private readonly System.Windows.Threading.DispatcherTimer _unloadTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private volatile bool _sessionEnding;
    private bool _updating;
    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private readonly CancellationTokenSource _updateCancellation = new();
    internal AppUpdater Updates { get; } = new(installedExe: AppUpdater.DefaultInstalledExe);
    internal static bool IsExiting { get; private set; }

    [STAThread]
    private static void Main()
    {
        // The workspace guard only waits for the top bar to end, so it skips the app entirely.
        if (TopBarProcess.RunGuard(Environment.GetCommandLineArgs().Skip(1).ToArray())) return;
        // Detached widget and wallpaper launches must never apply a pending update.
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false)
            .OnFirstRun(_ =>
            {
                if (Autostart.AutostartManager.IsEnabled()) Autostart.AutostartManager.SetEnabled(true);
            }).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("Unhandled UI exception.", args.Exception);
            WinForms.MessageBox.Show("Something went wrong: " + args.Exception.Message, "Belie",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error("Unhandled background exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
        SessionEnding += (_, args) =>
        {
            _sessionEnding = true;
            IsExiting = true;
            Logger.Warn($"Windows session ending ({args.ReasonSessionEnding}); shutting down.");
        };

        try
        {
            AppPaths.EnsureDirectories();
            Logger.Init(AppPaths.LogsDir);

            var barIndex = Array.FindIndex(e.Args, a => a.Equals(TopBarProcess.HostArgument, StringComparison.OrdinalIgnoreCase));
            if (barIndex >= 0 || e.Args.Any(a => a.Equals("--widget-host", StringComparison.OrdinalIgnoreCase)))
            {
                ConfigureRendering(liveWallpaper: false);
                // Widgets and bars come and go; only the hosts decide when to end.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                string? Argument(int index) => index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
                var bar = Argument(barIndex);
                var canvas = Argument(Array.FindIndex(e.Args, a => a.Equals("--canvas-file", StringComparison.OrdinalIgnoreCase)))
                    ?? (bar != null ? DesktopHosts.CanvasFileFor(bar) : DesktopCanvasProcess.DefaultFile);
                MemoryTrim.Soon();
                try { _desktopHosts = new DesktopHosts(canvas, bar ?? DesktopHosts.BarFileFor(canvas), Shutdown); }
                catch (Exception ex) { Logger.Error("Starting the desktop host failed.", ex); Shutdown(); }
                return;
            }

            if (HandleCommandLine(e.Args))
            {
                Shutdown();
                return;
            }

            _activateEvent = LocalIpc.Event(EventResetMode.AutoReset, ActivateEventName, out var eventIsNew);
            _mutex = LocalIpc.Mutex(true, MutexName, out var isFirst);
            if (!isFirst)
            {
                // Focus the running instance's window instead of nagging with a dialog.
                try
                {
                    if (e.Args.Any(a => a.Equals("--desktop-canvas", StringComparison.OrdinalIgnoreCase))
                        && EventWaitHandle.TryOpenExisting(@"Local\Belie.Canvas.Open", out var canvasEvent))
                    { using (canvasEvent) canvasEvent.Set(); }
                    else if (e.Args.Any(a => a.Equals("--topbar-settings", StringComparison.OrdinalIgnoreCase))
                        && EventWaitHandle.TryOpenExisting(TopBarProcess.SettingsEventName, out var barEvent))
                    { using (barEvent) barEvent.Set(); }
                    else _activateEvent.Set();
                }
                catch
                {
                }
                Logger.Info("Another instance is already running; asked it to bring up its window.");
                Shutdown();
                return;
            }

            var settingsStore = new SettingsStore(AppPaths.SettingsFile);
            ConfigureRendering(liveWallpaper: false, interactiveShell: true);
            _exitEvent = LocalIpc.Event(EventResetMode.AutoReset, @"Local\Belie.Exit");
            _canvasActivateEvent = LocalIpc.Event(EventResetMode.AutoReset, @"Local\Belie.Canvas.Open");
            _barSettingsEvent = LocalIpc.Event(EventResetMode.AutoReset, TopBarProcess.SettingsEventName);
            _barProfileEvent = LocalIpc.Event(EventResetMode.AutoReset, TopBarProcess.ProfileEventName);
            _wallpaperEvents = Enum.GetValues<WallpaperAction>().Select(action => LocalIpc.Event(EventResetMode.AutoReset, TopBarProcess.WallpaperEventName(action))).ToArray();
            var settings = settingsStore.Load();
            UiAppearance.Apply(settings.UiTheme);
            var profileStore = new ProfileStore(AppPaths.ProfilesDir);

            _coordinator = new WallpaperCoordinator(profileStore, settingsStore, settings);
            _coordinator.WallpaperChanged += (active, wallpaper) =>
            {
                try
                {
                    var canvasStore = new DesktopCanvasStore(DesktopCanvasProcess.DefaultFile);
                    if (!File.Exists(canvasStore.FilePath)) return;
                    var canvas = canvasStore.Load();
                    if (canvas.ActiveProfileId != active || !DesktopCanvasDocument.SameWallpaper(canvas.ActiveWallpaperPath, wallpaper))
                        canvasStore.Update(saved => { saved.ActiveProfileId = active; saved.ActiveWallpaperPath = wallpaper; });
                }
                catch (Exception ex) { Logger.Error("Updating profile widgets failed.", ex); }
            };
            _coordinator.Init();
            // The bar is about to start, so its keys are left to it from the beginning.
            ReadBar(starting: true);
            _profileShortcuts = new ProfileShortcutManager(() => _coordinator.Profiles,
                profileStore.Save, _coordinator.ProfilesEdited, ActivateProfileShortcut, workspaceFor: keys => _barSettings?.WorkspaceFor(keys));
            _coordinator.StateChanged += _profileShortcuts.Reload;

            _wallpaperKeys = new WallpaperKeys(ChangeWallpaper);
            _wallpaperKeys.Apply(settings);

            try { DesktopCanvasProcess.Start(DesktopCanvasProcess.DefaultFile); }
            catch (Exception ex) { Logger.Error("Restoring desktop widgets failed.", ex); }
            StartTopBar();
            var started = Environment.TickCount64;
            _barTimer.Tick += (_, _) =>
            {
                if (ReadBar(starting: Environment.TickCount64 - started < 15000)) _profileShortcuts?.Reload();
            };
            _barTimer.Start();

            var startMinimized = settings.StartMinimizedToTray
                || e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            _createMain = () => new MainWindow(_coordinator, profileStore, settingsStore, profileShortcuts: _profileShortcuts)
            {
                OpenSwitcher = () => ChangeWallpaper(WallpaperAction.Switcher),
                WallpaperKeysChanged = () => { _wallpaperKeys.Apply(settings); return _wallpaperKeys.Problem; }
            };
            _unloadTimer.Tick += (_, _) =>
            {
                if (IsExiting || _main?.TryUnload() != true) return; // Tries again later.
                _unloadTimer.Stop();
                _main = null;
                MemoryTrim.Soon();
            };
            MemoryTrim.Soon();
            _tray = new TrayIconManager(
                _coordinator.GetSnapshot,
                OpenMainWindow,
                id => _coordinator.SwitchManually(id),
                () => _coordinator.TogglePaused(),
                ExitApplication,
                ChangeWallpaper);
            _coordinator.StateChanged += () => _tray.Refresh();

            // Background watcher: a second launch signals this event to open the window.
            if (eventIsNew)
            {
                var watcher = new Thread(WatchForActivationRequests)
                {
                    IsBackground = true,
                    Name = "ActivationWatcher",
                };
                watcher.Start();
            }

            if (!startMinimized)
            {
                OpenMainWindow();
            }
            if (e.Args.Any(a => a.Equals("--desktop-canvas", StringComparison.OrdinalIgnoreCase)))
            { OpenMainWindow(); _main?.OpenCanvas(); }
            if (e.Args.Any(a => a.Equals("--topbar-settings", StringComparison.OrdinalIgnoreCase)))
            { OpenMainWindow(); _main?.OpenTopBarSettings(); }
            Logger.Info(startMinimized ? "Started minimized to tray." : "Started with main window visible.");
            Updates.UpdateFound += () => _tray?.ShowUpdateAvailable();
            // A build folder may have claimed the startup entry; only the installed copy can update, so it takes it back.
            if (Updates.IsInstalled && Autostart.AutostartManager.IsEnabled()) Autostart.AutostartManager.SetEnabled(true);
            _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = AppUpdater.CheckInterval };
            _updateTimer.Tick += (_, _) => CheckForUpdates();
            _updateTimer.Start();
            CheckForUpdates();
        }
        catch (Exception ex)
        {
            Logger.Error("Startup failed.", ex);
            WinForms.MessageBox.Show("Startup failed: " + ex.Message, "Belie",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            Shutdown(-1);
        }
    }

    private void WatchForActivationRequests()
    {
        while (_activateEvent != null && _canvasActivateEvent != null && _exitEvent != null
            && _barSettingsEvent != null && _barProfileEvent != null)
        {
            try
            {
                var request = WaitHandle.WaitAny(new WaitHandle[] { _activateEvent, _canvasActivateEvent, _exitEvent, _barSettingsEvent, _barProfileEvent }.Concat(_wallpaperEvents).ToArray());
                Dispatcher.Invoke(() =>
                {
                    if (request == 2) { ExitApplication(); return; }
                    if (request == 4) { ApplyWorkspaceProfile(); return; }
                    if (request > 4) { ChangeWallpaper((WallpaperAction)(request - 5)); return; }
                    OpenMainWindow();
                    if (request == 1) _main?.OpenCanvas();
                    if (request == 3) _main?.OpenTopBarSettings();
                });
                if (request == 2) return;
            }
            catch
            {
                // Dispatcher gone during shutdown.
            }
        }
    }

    /// <summary>Notes which workspace keys the bar holds. Returns whether that may have changed.</summary>
    private bool ReadBar(bool starting)
    {
        var store = new TopBarStore(TopBarProcess.DefaultFile);
        var state = (starting || TopBarProcess.IsRunning(store.FilePath), File.GetLastWriteTimeUtc(store.FilePath));
        if (state == _barState) return false;
        _barState = state;
        _barSettings = state.Item1 ? store.Load() : null;
        return true;
    }

    private static void StartTopBar()
    {
        try { TopBarProcess.Start(TopBarProcess.DefaultFile); }
        catch (Exception ex) { Logger.Error("Starting the top bar failed.", ex); }
    }

    private void ActivateProfileShortcut(Guid id)
    {
        if (_coordinator == null) return;
        try
        {
            var shortcut = _coordinator.Profiles.FirstOrDefault(profile => profile.Id == id)?.KeyboardShortcut ?? "";
            // The keys also open a workspace: show the profile with it, without stopping schedules and triggers.
            if (TopBarProcess.ForwardShortcut(TopBarProcess.DefaultFile, shortcut, id) is { } workspaceProfile)
            {
                _coordinator.ShowForWorkspace(workspaceProfile);
                return;
            }
        }
        catch (Exception ex) { Logger.Error("Opening a workspace from a profile shortcut failed.", ex); }
        _coordinator.SwitchManually(id);
    }

    private async void ChangeWallpaper(WallpaperAction action)
    {
        try
        {
            if (_coordinator == null) return;
            if (action != WallpaperAction.Switcher) { _coordinator.StepWallpaper(action == WallpaperAction.Next ? 1 : -1); return; }
            if (_switcher != null) { _switcher.CloseSmoothly(); return; }
            if ((_coordinator.ActiveProfile ?? _coordinator.Profiles.FirstOrDefault()) is not { } profile) { OpenMainWindow(); return; }
            var switcher = new WallpaperSwitcher(_coordinator.Settings, _coordinator.Profiles, profile, _coordinator.ChooseWallpaper,
                WinForms.Screen.FromPoint(WinForms.Cursor.Position).Bounds);
            _switcher = switcher;
            switcher.Closed += (_, _) => { if (_switcher == switcher) _switcher = null; MemoryTrim.Soon(); };
            await switcher.OpenAsync(_coordinator.CurrentMediaPath);
        }
        catch (Exception ex) { Logger.Error("The wallpaper switcher failed.", ex); _switcher = null; }
    }

    private void ApplyWorkspaceProfile()
    {
        try
        {
            var request = AtomicJsonFile.Read<WorkspaceProfileRequest>(TopBarProcess.ProfileRequestFile(TopBarProcess.DefaultFile));
            if (request == null || _coordinator == null) return;
            // Keys that are both a workspace's and a profile's: opening the workspace shows the profile without stopping
            // schedules and triggers. When no workspace opened, they were pressed for the profile.
            if (request.Shortcut.Length > 0 && _profileShortcuts?.OwnerOf(request.Shortcut) is { } owner)
            {
                var profile = request.ProfileId == Guid.Empty ? owner : request.ProfileId;
                if (request.OnlyWithShortcut) _coordinator.SwitchManually(profile);
                else _coordinator.ShowForWorkspace(profile);
            }
            // Opening a workspace is not a choice to stop schedules and triggers.
            else if (!request.OnlyWithShortcut && request.ProfileId != Guid.Empty)
                _coordinator.ShowForWorkspace(request.ProfileId);
        }
        catch (Exception ex) { Logger.Error("Applying a workspace profile failed.", ex); }
    }

    private void OpenMainWindow()
    {
        if (_main == null && _createMain != null)
        {
            _main = _createMain();
            _main.IsVisibleChanged += (_, _) =>
            {
                _unloadTimer.Stop();
                if (_main?.IsVisible == false) _unloadTimer.Start();
            };
        }
        if (_main == null)
        {
            return;
        }
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    internal static void ConfigureRendering(bool liveWallpaper, bool interactiveShell = false)
    {
        // Keep the background widget process lean without forcing the interactive shell to render on the CPU.
        System.Windows.Media.RenderOptions.ProcessRenderMode = liveWallpaper || interactiveShell
            ? System.Windows.Interop.RenderMode.Default
            : System.Windows.Interop.RenderMode.SoftwareOnly;
    }

    private void ExitApplication()
    {
        if (_main != null && !_main.TryLeaveAllDrafts()) return;
        IsExiting = true;
        try { DesktopCanvasProcess.Start(DesktopCanvasProcess.DefaultFile); }
        catch (Exception ex) { Logger.Error("Keeping desktop widgets running after tray exit failed.", ex); }
        StartTopBar();
        try
        {
            _tray?.Dispose();
        }
        catch
        {
        }
        Shutdown();
    }

    private async void CheckForUpdates()
    {
        await Updates.CheckAsync();
        if (_updateTimer != null) _updateTimer.Interval = Updates.NextCheck;
    }

    internal Task InstallUpdateAsync() => Updates.InstallAsync(
        () => _main?.TryLeaveAllDrafts() ?? true,
        () =>
        {
            _updating = true;
            IsExiting = true;
            DesktopCanvasProcess.Stop(DesktopCanvasProcess.DefaultFile);
            TopBarProcess.Stop(TopBarProcess.DefaultFile);
            LiveHostProcess.StopRunning();
            Shutdown();
        }, _updateCancellation.Token);

    protected override void OnExit(ExitEventArgs e)
    {
        _updateTimer?.Stop(); _barTimer.Stop();
        _updateCancellation.Cancel();
        Logger.Info("Shutting down.");
        if (_profileShortcuts != null && _coordinator != null) _coordinator.StateChanged -= _profileShortcuts.Reload;
        _profileShortcuts?.Dispose();
        _wallpaperKeys?.Dispose();
        _desktopHosts?.Dispose();
        _desktopHosts = null;
        // Hand the live wallpaper over to a detached host process so it keeps playing
        // after this process is gone (skipped when the session itself is ending).
        var handoff = _sessionEnding || _updating ? null : _coordinator?.CaptureLiveHandoff();
        try
        {
            _coordinator?.Dispose();
        }
        catch
        {
        }
        _coordinator = null;
        if (handoff != null)
        {
            LiveHostProcess.Launch(handoff.MediaPath, handoff.FitMode, handoff.Muted);
        }
        try
        {
            _tray?.Dispose();
        }
        catch
        {
        }
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // The current thread may not own the mutex (e.g. second-instance path); disposal still cleans up.
        }
        _mutex?.Dispose();
        // _activateEvent is intentionally left for the kernel to reclaim: the watcher
        // thread may still be blocked in WaitOne, and named handles die with the process.
        base.OnExit(e);
    }

    private bool HandleCommandLine(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals("--exit", StringComparison.OrdinalIgnoreCase))
            {
                if (EventWaitHandle.TryOpenExisting(@"Local\Belie.Exit", out var exit))
                { using (exit) exit.Set(); }
                return true;
            }
            if (args[i].Equals("--live-host", StringComparison.OrdinalIgnoreCase))
            {
                // Detached host started by a previous instance on exit: keep playing the
                // live wallpaper behind the desktop icons until signaled to stop.
                if (i + 1 >= args.Count)
                {
                    return true;
                }
                var mediaPath = args[++i];
                var fit = FitMode.Fill;
                var muted = true;
                for (var j = i + 1; j < args.Count; j++)
                {
                    if (args[j].Equals("--fit", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Count
                        && Enum.TryParse<FitMode>(args[j + 1], ignoreCase: true, out var parsedFit))
                    {
                        fit = parsedFit;
                        j++;
                    }
                    else if (args[j].Equals("--audible", StringComparison.OrdinalIgnoreCase))
                    {
                        muted = false;
                    }
                }
                RunLiveHost(mediaPath, fit, muted);
                return true;
            }

            if (args[i].Equals("--set-wallpaper", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    ShowUsage();
                    return true;
                }
                var imagePath = args[++i];
                var fit = FitMode.Fill;
                if (i + 2 < args.Count && args[i + 1].Equals("--fit", StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse<FitMode>(args[i + 2], ignoreCase: true, out var parsed))
                {
                    fit = parsed;
                }
                if (WallpaperEngine.IsLiveFile(imagePath))
                {
                    if (!File.Exists(imagePath))
                    {
                        WinForms.MessageBox.Show($"The file does not exist:\n{imagePath}", "Belie");
                        return true;
                    }
                    // Modal message boxes pump messages, so the video/GIF plays while it is open.
                    var live = new LiveWallpaperController();
                    live.StartVideo(imagePath, fit, muted: true);
                    Logger.Info("Command-line live wallpaper started.");
                    WinForms.MessageBox.Show(
                        "The live wallpaper is now playing behind your desktop icons.\n\n" +
                        "It stops when you click OK. To keep a live wallpaper active, save it as a profile instead.",
                        "Belie");
                    live.Stop();
                    return true;
                }
                var ok = WallpaperEngine.SetWallpaper(imagePath, fit);
                Logger.Info(ok ? "Command-line wallpaper applied successfully." : "Command-line wallpaper FAILED.");
                if (!ok)
                {
                    WinForms.MessageBox.Show(
                        $"Failed to set the wallpaper.\nSee the log for details:\n{AppPaths.LogsDir}",
                        "Belie");
                }
                return true;
            }

            if (args[i].Equals("--apply-profile", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    ShowUsage();
                    return true;
                }
                var wanted = args[++i];
                var store = new ProfileStore(AppPaths.ProfilesDir);
                var profile = store.LoadAll().FirstOrDefault(p => p.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                {
                    WinForms.MessageBox.Show($"No profile named \"{wanted}\" was found.", "Belie");
                    return true;
                }
                if (WallpaperEngine.IsLiveFile(profile.FolderPath))
                {
                    if (!File.Exists(profile.FolderPath))
                    {
                        WinForms.MessageBox.Show($"The profile's file does not exist:\n{profile.FolderPath}",
                            "Belie");
                        return true;
                    }
                    var live = new LiveWallpaperController();
                    live.StartVideo(profile.FolderPath, profile.FitMode, profile.VideoMuted);
                    Logger.Info($"Command-line live wallpaper started for profile '{profile.Name}'.");
                    WinForms.MessageBox.Show(
                        $"The live wallpaper of profile \"{profile.Name}\" is playing behind your desktop icons.\n\n" +
                        "It stops when you click OK.",
                        "Belie");
                    live.Stop();
                    return true;
                }
                var images = WallpaperEngine.GetImages(profile.FolderPath);
                if (images.Count == 0)
                {
                    WinForms.MessageBox.Show($"The profile's folder has no usable images:\n{profile.FolderPath}",
                        "Belie");
                    return true;
                }
                WallpaperEngine.SetWallpaper(images[0], profile.FitMode);
                return true;
            }

            if (args[i].Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                ShowUsage();
                return true;
            }
        }
        return false;
    }

    private static void RunLiveHost(string mediaPath, FitMode fitMode, bool muted)
    {
        try
        {
            if (!File.Exists(mediaPath))
            {
                Logger.Warn($"Live host media not found: '{mediaPath}'. Nothing to play.");
                return;
            }
            // Create the stop event before anything else so the main app can always signal it.
            using var stopEvent = LocalIpc.Event(EventResetMode.AutoReset, LiveHostProcess.StopEventName);
            var surface = new LiveWallpaperSurface(mediaPath, fitMode, muted, onClosed: null);
            if (!surface.Show())
            {
                Logger.Warn("The live wallpaper host could not attach to the desktop; exiting.");
                return;
            }
            Logger.Info("Detached live wallpaper host is playing.");
            // Wait until the main app takes over (stop event) or the surface closes itself.
            _ = WaitHandle.WaitAny([stopEvent, surface.ExitedHandle]);
            surface.Dispose();
            Logger.Info("Detached live wallpaper host stopped.");
        }
        catch (Exception ex)
        {
            Logger.Error("The live wallpaper host failed.", ex);
        }
    }

    private static void ShowUsage()
    {
        WinForms.MessageBox.Show(
            "Belie\n\n" +
            "Normally you just run the app and use the system tray icon.\n\n" +
            "Optional command-line switches:\n" +
            "  --set-wallpaper \"<image or video path>\" [--fit fill|fit|stretch|tile|center|span]\n" +
            "      (video/GIF files play as a live wallpaper until you press OK)\n" +
            "  --apply-profile \"<profile name>\"\n" +
            "  --minimized         start hidden in the tray\n" +
            "  --live-host \"<video path>\" [--fit …] [--audible]\n" +
            "      (internal: keeps a live wallpaper playing after the app exits)",
            "Belie");
    }
}
