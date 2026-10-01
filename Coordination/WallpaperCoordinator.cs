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

    public sealed record ActivityEntry(DateTime AtLocal, string ProfileName, string Reason);

    public sealed record ActivitySnapshot(WallpaperProfile? ActiveProfile, string Reason, bool Paused,
        DateTime? NextScheduleAtLocal, string? NextScheduledProfile, ManualOverrideStatus? ManualOverride,
        IReadOnlyList<ActivityEntry> Recent);

    private sealed record ActiveEvent(Guid ProfileId, EventTrigger Trigger, DateTime SinceUtc);

    private readonly ProfileStore _store;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly ProfileResolver _resolver;
    private readonly TimeProvider _timeProvider;
    private readonly List<ActivityEntry> _recent = new();
    private readonly SceneController _scene = new();
    private readonly Action<WallpaperProfile> _applyWallpaper;
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
    private DesktopCanvasStore? _canvas;
    private DispatcherTimer? _canvasTimer;
    private string _canvasKey = "";
    private bool _canvasOwnsWallpaper;
    private string _canvasBoardName = "";

    public event Action? StateChanged;

    public WallpaperCoordinator(ProfileStore store, SettingsStore settingsStore, AppSettings settings,
        TimeProvider? timeProvider = null, Action<WallpaperProfile>? applyWallpaper = null)
    {
        _store = store;
        _settingsStore = settingsStore;
        _settings = settings;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _resolver = new ProfileResolver(_timeProvider);
        _applyWallpaper = applyWallpaper ?? ApplyWallpaper;
        _scene.StateChanged += () => StateChanged?.Invoke();
        _triggers = new TriggerManager();
        _triggers.TriggerStateChanged += OnTriggerStateChanged;
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _tickTimer.Tick += (_, _) => CheckNow();
    }

    public IReadOnlyList<WallpaperProfile> Profiles => _profiles;

    public Guid? ActiveProfileId => _currentId;

    public bool Paused { get; private set; }

    public string Summary => _summary;
    public DateTime LocalNow => _timeProvider.GetLocalNow().DateTime;
    public string AmbientStatus => _scene.AudioStatus;
    public bool HasAmbientAudio => _scene.HasAudio;
    public bool AmbientIsMuted => _scene.IsMuted;
    public void ToggleAmbientMuted() => _scene.ToggleMuted();
    public void CheckNow() => Evaluate("periodic check");

    public void AttachCanvas(DesktopCanvasStore canvas)
    {
        _canvas = canvas;
        _canvasTimer?.Stop();
        _canvasTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _canvasTimer.Tick += (_, _) => RefreshCanvasBoard();
        RefreshCanvasBoard(); _canvasTimer.Start();
    }
    private void RefreshCanvasBoard()
    {
        try
        {
            var document = _canvas!.Load();
            if (!document.Enabled) return;
            if (document.ApplyBoardSchedule(LocalNow))
            { _canvas.Update(latest => latest.ApplyBoardSchedule(LocalNow)); document = _canvas.Load(); }
            var board = document.ActiveBoard!;
            var key = DesktopCanvasFeatures.WallpaperKey(board, LocalNow);
            if (key == _canvasKey) return;
            var scene = DesktopCanvasFeatures.SceneForBoard(board, LocalNow);
            if (string.IsNullOrWhiteSpace(board.WallpaperPath) && board.Id != Guid.Empty)
            {
                _canvasKey = key; _slideshow.Stop(); _live.Stop(); _iconSafeLive.Stop(); _scene.Dispose();
                _canvasOwnsWallpaper = true; _canvasBoardName = board.Name; UpdateSummary(); return;
            }
            if (!File.Exists(scene.FolderPath) && !Directory.Exists(scene.FolderPath)) return;
            _canvasKey = key;
            _canvasOwnsWallpaper = true; _canvasBoardName = board.Name;
            _scene.Apply(scene); _applyWallpaper(scene);
            RecordActivity(board.Name, "Desktop board switched"); UpdateSummary();
        }
        catch (Exception ex) { Logger.Error("Applying desktop board failed; saved boards were preserved.", ex); }
    }

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
            Activate(profile, ResolutionSource.Restored);
        }
    }

    public void SwitchManually(Guid profileId, TimeSpan? duration = null)
    {
        if (!_profiles.Any(p => p.Id == profileId)) return;
        _canvasOwnsWallpaper = false;
        _resolver.SetManual(profileId, duration);
        if (duration.HasValue)
        {
            // A temporary choice must return to the existing default when no rule matches.
            _fallbackProfileId ??= _currentId;
        }
        else
        {
            _fallbackProfileId = profileId;
            _settings.PreferredProfileId = profileId.ToString();
            SaveSettingsSafe();
        }
        Logger.Info($"Manual switch requested -> {DescribeProfile(profileId)}.");
        Evaluate("manual switch", forceReapply: true);
    }

    public void TogglePaused()
    {
        Paused = !Paused;
        Logger.Info(Paused ? "Scheduling paused by user." : "Scheduling resumed by user.");
        if (!Paused)
        {
            RecordActivity(DescribeProfile(_currentId), "Automation resumed");
            Evaluate("resumed");
        }
        else
        {
            RecordActivity(DescribeProfile(_currentId), "Automation paused");
            UpdateSummary();
        }
    }

    public void ResumeAutomatic()
    {
        _canvasOwnsWallpaper = false;
        _resolver.ClearManual();
        Paused = false;
        RecordActivity(DescribeProfile(_currentId), "Returned to automatic rules");
        Evaluate("returned to automation");
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
            _scene.Dispose();
            _settings.LastActiveProfileId = "";
            settingsChanged = true;
        }
        if (settingsChanged)
        {
            SaveSettingsSafe();
        }
        Evaluate("profiles changed", forceReapply: true);
    }

    public Snapshot GetSnapshot() => new(_profiles, _currentId, Paused, _summary);

    public ActivitySnapshot GetActivitySnapshot()
    {
        var next = ProfileResolver.NextBoundary(_timeProvider.GetLocalNow().DateTime, _profiles);
        return new ActivitySnapshot(_profiles.FirstOrDefault(p => p.Id == _currentId), ActivationReason(), Paused,
            next, next.HasValue ? ProfileResolver.ScheduledProfileAt(next.Value, _profiles)?.Name : null,
            _resolver.GetManualOverride(_profiles), _recent.ToArray());
    }

    /// <summary>What the smooth live overlay is currently playing, for the exit handoff.</summary>
    public sealed record LiveHandoff(string MediaPath, FitMode FitMode, bool Muted);

    public LiveHandoff? CaptureLiveHandoff()
    {
        if (_canvasOwnsWallpaper) return null;
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
        if (_canvasOwnsWallpaper) { UpdateSummary(); return; }
        var now = _timeProvider.GetLocalNow().DateTime;
        var manualBefore = _resolver.GetManualOverride(_profiles);
        var decision = _resolver.Resolve(now, _profiles, SnapshotActiveEvents(), _fallbackProfileId);
        if (manualBefore != null && _resolver.GetManualOverride(_profiles) == null)
            RecordActivity(DescribeProfile(manualBefore.ProfileId), "Manual override ended");
        // Pausing freezes automatic changes; an explicit manual switch still works.
        if (Paused && decision.Source != ResolutionSource.Manual)
        {
            var held = _profiles.FirstOrDefault(p => p.Id == _currentId);
            if (forceReapply && held != null) Activate(held, _source == ResolutionSource.Manual ? ResolutionSource.Retained : _source);
            else UpdateSummary();
            return;
        }
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
                if (_source != decision.Source)
                {
                    _source = decision.Source;
                    RecordActivity(profile.Name, ActivationReason());
                }
                _source = decision.Source;
            }
        }
        else if (_currentId != null)
        {
            _source = ResolutionSource.Retained;
        }
        UpdateSummary();
    }

    private IReadOnlyCollection<ActiveEventSnapshot> SnapshotActiveEvents()
        => _activeEvents.Values
            .Select(e => new ActiveEventSnapshot(e.ProfileId, e.Trigger, e.Trigger.Priority, e.SinceUtc))
            .ToList();

    private void Activate(WallpaperProfile profile, ResolutionSource source)
    {
        _currentId = profile.Id;
        _source = source;
        _scene.Apply(profile);
        _applyWallpaper(profile);
        var temporary = source == ResolutionSource.Manual && _resolver.GetManualOverride(_profiles)?.IsTimed == true;
        if (!temporary) _settings.LastActiveProfileId = profile.Id.ToString();
        if (source == ResolutionSource.Manual && !temporary)
        {
            _fallbackProfileId = profile.Id;
            _settings.PreferredProfileId = profile.Id.ToString();
        }
        SaveSettingsSafe();
        Logger.Info($"Active profile is now '{profile.Name}' (via {source}).");
        RecordActivity(profile.Name, ActivationReason());
        UpdateSummary();
    }

    private void ApplyWallpaper(WallpaperProfile profile)
    {
        // A detached live host (started when the app last exited) must never outlive an
        // activation: this profile is taking over the desktop now.
        LiveHostProcess.StopRunning();
        _slideshow.Stop();
        // Invalidate any wallpaper apply still queued for the previous profile.
        WallpaperEngine.NextApplyGeneration();
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
    }

    private string ActivationReason() => _currentId == null ? "Choose a profile to get started." : _source switch
    {
        ResolutionSource.Manual => _resolver.GetManualOverride(_profiles)?.IsTimed == true ? "Temporary manual override" : "Manual choice",
        ResolutionSource.Event => "Event trigger: " + (_activeEvents.Values.Where(e => e.ProfileId == _currentId)
            .OrderByDescending(e => e.Trigger.Priority).ThenByDescending(e => e.SinceUtc).FirstOrDefault()?.Trigger.Describe() ?? "active condition"),
        ResolutionSource.Fallback => "Your default profile — no schedule or event is active",
        ResolutionSource.Retained => "No automatic rule matched — keeping the current wallpaper",
        ResolutionSource.Restored => "Restored your previous profile",
        _ => "Active schedule rule"
    };

    private void RecordActivity(string profileName, string reason)
    {
        _recent.Insert(0, new ActivityEntry(_timeProvider.GetLocalNow().DateTime, profileName, reason));
        if (_recent.Count > 20) _recent.RemoveAt(_recent.Count - 1);
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
        if (_canvasOwnsWallpaper)
        { _summary = "Active board: " + _canvasBoardName; StateChanged?.Invoke(); return; }
        var state = Paused ? "PAUSED" : "auto";
        var sourceText = _source switch
        {
            ResolutionSource.Manual => "manual",
            ResolutionSource.Event => "event",
            ResolutionSource.Fallback => "default",
            ResolutionSource.Retained => "unchanged",
            ResolutionSource.Restored => "restored",
            _ => "schedule",
        };
        var next = ProfileResolver.NextBoundary(_timeProvider.GetLocalNow().DateTime, _profiles);
        var boundaryText = next.HasValue ? $" | next boundary {next.Value:g}" : "";
        var liveText = _profiles.FirstOrDefault(p => p.Id == _currentId) is { } current
            && WallpaperEngine.IsLiveFile(current.FolderPath) ? " | live" : "";
        _summary = $"Active: {DescribeProfile(_currentId)} ({sourceText}) | Mode: {state}{liveText}{boundaryText}";
        _tickTimer.Interval = _resolver.GetManualOverride(_profiles)?.IsTimed == true ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(20);
        StateChanged?.Invoke();
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
        _canvasTimer?.Stop();
        _triggers.Dispose();
        _slideshow.Dispose();
        _live.Dispose();
        _iconSafeLive.Dispose();
        _scene.Dispose();
    }
}
