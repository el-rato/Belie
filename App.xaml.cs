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
    private DesktopCanvasHost? _widgetHost;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _canvasActivateEvent;
    private volatile bool _sessionEnding;

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
            Logger.Warn($"Windows session ending ({args.ReasonSessionEnding}); shutting down.");
        };

        try
        {
            AppPaths.EnsureDirectories();
            Logger.Init(AppPaths.LogsDir);

            if (e.Args.Any(a => a.Equals("--widget-host", StringComparison.OrdinalIgnoreCase)))
            {
                var fileIndex = Array.FindIndex(e.Args, a => a.Equals("--canvas-file", StringComparison.OrdinalIgnoreCase));
                var file = fileIndex >= 0 && fileIndex + 1 < e.Args.Length ? e.Args[fileIndex + 1] : DesktopCanvasProcess.DefaultFile;
                try { _widgetHost = new DesktopCanvasHost(file); }
                catch (Exception ex) { Logger.Error("Starting desktop widgets failed.", ex); Shutdown(); }
                return;
            }

            if (HandleCommandLine(e.Args))
            {
                Shutdown();
                return;
            }

            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName, out var eventIsNew);
            _mutex = new Mutex(true, MutexName, out var isFirst);
            if (!isFirst)
            {
                // Focus the running instance's window instead of nagging with a dialog.
                try
                {
                    if (e.Args.Any(a => a.Equals("--desktop-canvas", StringComparison.OrdinalIgnoreCase))
                        && EventWaitHandle.TryOpenExisting(@"Local\Belie.Canvas.Open", out var canvasEvent))
                    { using (canvasEvent) canvasEvent.Set(); }
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
            _canvasActivateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Belie.Canvas.Open");
            var settings = settingsStore.Load();
            var profileStore = new ProfileStore(AppPaths.ProfilesDir);

            _coordinator = new WallpaperCoordinator(profileStore, settingsStore, settings);
            _coordinator.Init();

            try { DesktopCanvasProcess.Start(DesktopCanvasProcess.DefaultFile); }
            catch (Exception ex) { Logger.Error("Restoring desktop widgets failed.", ex); }

            var startMinimized = settings.StartMinimizedToTray
                || e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            _main = new MainWindow(_coordinator, profileStore, settingsStore);
            _tray = new TrayIconManager(
                _coordinator.GetSnapshot,
                OpenMainWindow,
                id => _coordinator.SwitchManually(id),
                () => _coordinator.TogglePaused(),
                ExitApplication);
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
            { OpenMainWindow(); _main.OpenCanvas(); }
            Logger.Info(startMinimized ? "Started minimized to tray." : "Started with main window visible.");
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
        while (_activateEvent != null && _canvasActivateEvent != null)
        {
            try
            {
                var request = WaitHandle.WaitAny(new WaitHandle[] { _activateEvent, _canvasActivateEvent });
                Dispatcher.Invoke(() => { OpenMainWindow(); if (request == 1) _main?.OpenCanvas(); });
            }
            catch
            {
                // Dispatcher gone during shutdown.
            }
        }
    }

    private void OpenMainWindow()
    {
        if (_main == null)
        {
            return;
        }
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void ExitApplication()
    {
        try
        {
            _tray?.Dispose();
        }
        catch
        {
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("Shutting down.");
        _widgetHost?.Dispose();
        _widgetHost = null;
        // Hand the live wallpaper over to a detached host process so it keeps playing
        // after this process is gone (skipped when the session itself is ending).
        var handoff = _sessionEnding ? null : _coordinator?.CaptureLiveHandoff();
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
            using var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, LiveHostProcess.StopEventName);
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
