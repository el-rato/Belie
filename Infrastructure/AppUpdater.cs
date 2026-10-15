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
    private readonly UpdateManager _manager = new(
        new GithubSource($"https://github.com/{AppUpdater.Repository}", accessToken: null, prerelease: false));

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
    private IAppUpdateClient? _client;
    public static Version CurrentVersion => typeof(AppUpdater).Assembly.GetName().Version!;
    public UpdateInfo? Available { get; private set; }
    public bool Busy { get; private set; }
    public string Status { get; private set; } = $"Installed version {CurrentVersion.ToString(3)}";
    public event Action? Changed;
    public event Action? UpdateFound;

    public AppUpdater(IAppUpdateClient? client = null) => _client = client;

    private IAppUpdateClient Client => _client ??= new VelopackUpdateClient();

    private bool CanUpdate()
    {
        if (Client.IsInstalled) return true;
        Available = null;
        SetStatus("Install Belie using Belie-win-Setup.exe, or extract the complete portable ZIP, to enable in-app updates.");
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
            var notify = release != null && release.TargetFullRelease.Version != Available?.TargetFullRelease.Version;
            Available = release;
            SetStatus(release == null ? "You’re up to date." : $"Version {release.TargetFullRelease.Version} is available.");
            if (notify) UpdateFound?.Invoke();
        }
        catch (Exception ex)
        {
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
