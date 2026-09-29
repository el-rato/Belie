using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Resolution;
using WallpaperProfiles.Triggers;

namespace WallpaperProfiles.Coordination;

internal sealed class WallpaperCoordinator : IDisposable
{
    public sealed record Snapshot(IReadOnlyList<WallpaperProfile> Profiles, Guid? ActiveProfileId, bool Paused, string Summary);

    private sealed record ActiveEvent(Guid ProfileId, EventTrigger Trigger, DateTime SinceUtc);

    private readonly ProfileStore _store;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly ProfileResolver _resolver = new();
    private readonly SlideshowController _slideshow = new();
    private readonly LiveWallpaperController _live = new();
    private readonly IconSafeLiveController _iconSafeLive = new();
    private readonly TriggerManager _triggers;
    private readonly DispatcherTimer _tickTimer;
    private List<WallpaperProfile> _profiles = new();
    private Guid? _currentId;
    private Guid? _fallbackProfileId;
    private ResolutionSource _source = ResolutionSource.Schedule;
    private readonly Dictionary<Guid, ActiveEvent> _activeEvents = new();
    private string _summary = "Starting…";

    public event Action? StateChanged;

    public WallpaperCoordinator(ProfileStore store, SettingsStore settingsStore, AppSettings settings)
    {
        _store = store;
        _settingsStore = settingsStore;
        _settings = settings;
        _triggers = new TriggerManager();
        _triggers.TriggerStateChanged += OnTriggerStateChanged;
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _tickTimer.Tick += (_, _) => Evaluate("periodic check");
    }

    public IReadOnlyList<WallpaperProfile> Profiles => _profiles;

    public Guid? ActiveProfileId => _currentId;

    public bool Paused { get; private set; }

    public string Summary => _summary;

    public void Init()
    {
        _profiles = _store.LoadAll();
        RestoreLastActive();
        _triggers.Reload(_profiles);
        Evaluate("startup");
        _tickTimer.Start();
        UpdateSummary();
    }

    private void RestoreLastActive()
    {
        var preferredProfile = Guid.TryParse(_settings.PreferredProfileId, out var preferredId)
            ? _profiles.FirstOrDefault(p => p.Id == preferredId)
            : null;
        if (preferredProfile == null && Guid.TryParse(_settings.LastActiveProfileId, out var lastActiveId))
        {
            preferredProfile = _profiles.FirstOrDefault(p => p.Id == lastActiveId);
            if (preferredProfile != null)
            {
                _settings.PreferredProfileId = preferredProfile.Id.ToString();
                SaveSettingsSafe();
            }
        }
        _fallbackProfileId = preferredProfile?.Id;

        if (!Guid.TryParse(_settings.LastActiveProfileId, out var id))
        {
            return;
        }
        var profile = _profiles.FirstOrDefault(p => p.Id == id);
        if (profile != null)
        {
            Activate(profile, ResolutionSource.Schedule);
        }
    }

    public void SwitchManually(Guid profileId)
    {
        _resolver.SetManual(profileId);
        _fallbackProfileId = profileId;
        _settings.PreferredProfileId = profileId.ToString();
        SaveSettingsSafe();
        Logger.Info($"Manual switch requested -> {DescribeProfile(profileId)}.");
        Evaluate("manual switch", forceReapply: true);
    }

    public void TogglePaused()
    {
        Paused = !Paused;
        Logger.Info(Paused ? "Scheduling paused by user." : "Scheduling resumed by user.");
        if (!Paused)
        {
            Evaluate("resumed");
        }
        else
        {
            UpdateSummary();
        }
    }

    public void ProfilesEdited()
    {
        _profiles = _store.LoadAll();
        _activeEvents.Clear();
        _triggers.Reload(_profiles);
        var settingsChanged = false;
        if (_fallbackProfileId.HasValue && !_profiles.Any(p => p.Id == _fallbackProfileId.Value))
        {
            _fallbackProfileId = null;
            _settings.PreferredProfileId = "";
            settingsChanged = true;
        }
        if (_currentId.HasValue && !_profiles.Any(p => p.Id == _currentId.Value))
        {
            Logger.Warn("Active profile was deleted; leaving wallpaper unchanged until the next automatic choice.");
            _currentId = null;
            _settings.LastActiveProfileId = "";
            settingsChanged = true;
        }
        if (settingsChanged)
        {
            SaveSettingsSafe();
        }
        Evaluate("profiles changed", forceReapply: true);
        StateChanged?.Invoke();
    }

    public Snapshot GetSnapshot() => new(_profiles, _currentId, Paused, _summary);

    /// <summary>What the smooth live overlay is currently playing, for the exit handoff.</summary>
    public sealed record LiveHandoff(string MediaPath, FitMode FitMode, bool Muted);

    public LiveHandoff? CaptureLiveHandoff()
    {
        var current = _profiles.FirstOrDefault(p => p.Id == _currentId);
        if (current == null || current.IconFriendlyLive || !WallpaperEngine.IsLiveFile(current.FolderPath))
        {
            return null;
        }
        // The icons-friendly mode pushes frames into the real wallpaper, which naturally
        // persists after exit — only the smooth overlay needs the detached host.
        return _live.CaptureInfo() is { } info ? new LiveHandoff(info.MediaPath, info.Fit, info.Muted) : null;
    }

    private void OnTriggerStateChanged(Guid profileId, EventTrigger trigger, bool active)
    {
        if (active)
        {
            _activeEvents[trigger.Id] = new ActiveEvent(profileId, trigger, DateTime.UtcNow);
        }
        else
        {
            _activeEvents.Remove(trigger.Id);
        }
        Logger.Info($"Event trigger {(active ? "ACTIVATED" : "cleared")}: {trigger.Describe()} (profile '{DescribeProfile(profileId)}').");
        Evaluate("event trigger change");
    }

    private void Evaluate(string reason, bool forceReapply = false)
    {
        var decision = _resolver.Resolve(DateTime.Now, _profiles, SnapshotActiveEvents(), _fallbackProfileId);
        if (decision.ProfileId is Guid id)
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == id);
            if (profile == null)
            {
                Logger.Warn($"Resolved to a missing profile {id}; clearing manual override for this round.");
                _resolver.ClearManual();
            }
            else if (id != _currentId || forceReapply)
            {
                Activate(profile, decision.Source);
                return;
            }
            else
            {
                _source = decision.Source;
            }
        }
        UpdateSummary();
    }

    private IReadOnlyCollection<ActiveEventSnapshot> SnapshotActiveEvents()
        => _activeEvents.Values
            .Select(e => new ActiveEventSnapshot(e.ProfileId, e.Trigger, e.Trigger.Priority, e.SinceUtc))
            .ToList();

    private void Activate(WallpaperProfile profile, ResolutionSource source)
    {
        // A detached live host (started when the app last exited) must never outlive an
        // activation: this profile is taking over the desktop now.
        LiveHostProcess.StopRunning();
        _slideshow.Stop();
        // Invalidate any wallpaper apply still queued for the previous profile.
        WallpaperEngine.NextApplyGeneration();
        _currentId = profile.Id;
        _source = source;
        if (WallpaperEngine.IsLiveFile(profile.FolderPath))
        {
            // Live wallpaper: either the smooth overlay (covers the desktop icons) or the
            // icons-friendly mode that updates the real wallpaper itself.
            if (profile.IconFriendlyLive)
            {
                _live.Stop();
                _iconSafeLive.Start(profile);
            }
            else
            {
                _iconSafeLive.Stop();
                _live.Start(profile);
            }
        }
        else
        {
            _live.Stop();
            _iconSafeLive.Stop();
            _slideshow.Start(profile);
        }
        _settings.LastActiveProfileId = profile.Id.ToString();
        if (source == ResolutionSource.Manual)
        {
            _fallbackProfileId = profile.Id;
            _settings.PreferredProfileId = profile.Id.ToString();
        }
        SaveSettingsSafe();
        Logger.Info($"Active profile is now '{profile.Name}' (via {source}).");
        UpdateSummary();
        StateChanged?.Invoke();
    }

    private void SaveSettingsSafe()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            Logger.Error("Saving settings.json failed.", ex);
        }
    }

    private void UpdateSummary()
    {
        var state = Paused ? "PAUSED" : "auto";
        var sourceText = _source switch
        {
            ResolutionSource.Manual => "manual",
            ResolutionSource.Event => "event",
            ResolutionSource.Fallback => "default",
            _ => "schedule",
        };
        var next = ProfileResolver.NextBoundary(DateTime.Now, _profiles);
        var boundaryText = next.HasValue ? $" | next boundary {next.Value:g}" : "";
        var liveText = _profiles.FirstOrDefault(p => p.Id == _currentId) is { } current
            && WallpaperEngine.IsLiveFile(current.FolderPath) ? " | live" : "";
        _summary = $"Active: {DescribeProfile(_currentId)} ({sourceText}) | Mode: {state}{liveText}{boundaryText}";
    }

    private string DescribeProfile(Guid? id)
    {
        if (id == null)
        {
            return "(none)";
        }
        var profile = _profiles.FirstOrDefault(x => x.Id == id.Value);
        return profile?.Name ?? "(deleted)";
    }

    public void Dispose()
    {
        _tickTimer.Stop();
        _triggers.Dispose();
        _slideshow.Dispose();
        _live.Dispose();
        _iconSafeLive.Dispose();
    }
}
