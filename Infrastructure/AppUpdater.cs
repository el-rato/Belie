using Velopack;
using Velopack.Sources;

namespace WallpaperProfiles.Infrastructure;

internal interface IAppUpdateClient
{
    bool IsInstalled { get; }
    Task<UpdateInfo?> CheckAsync();
    Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken);
    void ApplyOnExit(UpdateInfo update);
}

internal sealed class VelopackUpdateClient : IAppUpdateClient
{
    // Reads the newest release's feed through github.com's download redirect rather than the GitHub API,
    // which allows only 60 unauthenticated requests an hour per network and needs several per check.
    private readonly UpdateManager _manager = new(new SimpleWebSource(AppUpdater.FeedUrl));

    public bool IsInstalled => _manager.IsInstalled;
    public Task<UpdateInfo?> CheckAsync() => _manager.CheckForUpdatesAsync();
    public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken) =>
        _manager.DownloadUpdatesAsync(update, progress, cancellationToken);
    public void ApplyOnExit(UpdateInfo update) =>
        _manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: true, restartArgs: Array.Empty<string>());
}

internal sealed class AppUpdater
{
    internal const string Repository = "el-rato/Belie";
    /// <summary>Serves releases.win.json and the packages of the newest published release.</summary>
    internal const string FeedUrl = "https://github.com/" + Repository + "/releases/latest/download/";
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    /// <summary>The network is often not up yet when Belie starts with Windows.</summary>
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    private readonly string? _installedExe;
    private IAppUpdateClient? _client;
    public static Version CurrentVersion => typeof(AppUpdater).Assembly.GetName().Version!;
    public UpdateInfo? Available { get; private set; }
    public bool Busy { get; private set; }
    public bool LastCheckFailed { get; private set; }
    public TimeSpan NextCheck => LastCheckFailed ? RetryInterval : CheckInterval;
    public string Status { get; private set; } = $"Installed version {CurrentVersion.ToString(3)}";
    public event Action? Changed;
    public event Action? UpdateFound;

    public AppUpdater(IAppUpdateClient? client = null, string? installedExe = null)
    {
        _client = client;
        _installedExe = installedExe;
    }

    /// <summary>Where Belie-win-Setup.exe puts Belie.</summary>
    internal static string DefaultInstalledExe => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Belie", "current", "Belie.exe");

    /// <summary>True for an installed or complete portable copy, which can update itself; false for a build folder.</summary>
    public bool IsInstalled
    {
        get
        {
            try { return Client.IsInstalled; }
            catch (Exception ex) { Logger.Error("Reading the installation failed.", ex); return false; }
        }
    }

    private IAppUpdateClient Client => _client ??= new VelopackUpdateClient();

    private bool CanUpdate()
    {
        if (Client.IsInstalled) return true;
        Available = null;
        // Only one Belie runs at a time, so the shortcut of the installed copy opens this one instead.
        SetStatus(_installedExe != null && File.Exists(_installedExe)
            ? $"This copy can’t update itself because it isn’t the installed one. It runs from {Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}. Exit Belie from the tray, then open Belie from the Start menu to get updates."
            : "Install Belie using Belie-win-Setup.exe, or extract the complete portable ZIP, to enable in-app updates.");
        return false;
    }

    public async Task CheckAsync()
    {
        if (Busy) return;
        Busy = true;
        SetStatus("Checking for updates…");
        try
        {
            if (!CanUpdate()) return;
            var release = await Client.CheckAsync();
            LastCheckFailed = false;
            var notify = release != null && release.TargetFullRelease.Version != Available?.TargetFullRelease.Version;
            Available = release;
            SetStatus(release == null ? "You’re up to date." : $"Version {release.TargetFullRelease.Version} is available.");
            if (notify) UpdateFound?.Invoke();
        }
        catch (Exception ex)
        {
            LastCheckFailed = true;
            Logger.Error("Checking for updates failed.", ex);
            SetStatus("Couldn’t check for updates. Check your connection and try again.");
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task InstallAsync(Func<bool> canRestart, Action restart, CancellationToken cancellationToken)
    {
        if (Busy || Available == null) return;
        Busy = true;
        SetStatus("Preparing update…");
        try
        {
            if (!CanUpdate()) return;
            var release = Available!;
            cancellationToken.ThrowIfCancellationRequested();
            // Progress callbacks originate on worker threads; marshal UI changes to the caller.
            var progress = new Progress<int>(percent =>
            {
                if (Busy && !cancellationToken.IsCancellationRequested)
                    SetStatus($"Downloading update… {percent}%");
            });
            await Client.DownloadAsync(release, percent => ((IProgress<int>)progress).Report(percent), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!canRestart()) { SetStatus("Update postponed. Your current version is still running."); return; }
            SetStatus("Restarting to install…");
            // Let WPF close cleanly and stop wallpaper/widget hosts before Velopack replaces files.
            Client.ApplyOnExit(release);
            restart();
        }
        catch (OperationCanceledException) { SetStatus("Update cancelled."); }
        catch (Exception ex)
        {
            Logger.Error("Installing update failed.", ex);
            SetStatus("Couldn’t install the update. Please try again.");
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    private void SetStatus(string status) { Status = status; Changed?.Invoke(); }
}
