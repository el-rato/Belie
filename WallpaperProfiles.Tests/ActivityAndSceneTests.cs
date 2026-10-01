using System.IO;
using System.Windows;
using System.Windows.Media;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Resolution;
using Xunit;
using EventTrigger = WallpaperProfiles.Models.EventTrigger;

namespace WallpaperProfiles.Tests;

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Advance(TimeSpan duration) => _now += duration;
}

public sealed class ActivityAndSceneTests
{
    [Fact]
    public void TimedOverride_HoldsAcrossScheduleBoundary_AndExpiresAtItsDeadline()
    {
        var clock = new TestClock();
        var resolver = new ProfileResolver(clock);
        var manual = new WallpaperProfile { Name = "Temporary" };
        var work = new WallpaperProfile { Name = "Work", Schedule = new() { DailyRule(9, 5, 17, 0) } };
        resolver.SetManual(manual.Id, TimeSpan.FromMinutes(30));
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(manual.Id, resolver.Resolve(clock.GetLocalNow().DateTime, new[] { manual, work }, Array.Empty<ActiveEventSnapshot>()).ProfileId);
        clock.Advance(TimeSpan.FromMinutes(20));
        var result = resolver.Resolve(clock.GetLocalNow().DateTime, new[] { manual, work }, Array.Empty<ActiveEventSnapshot>());
        Assert.Equal(work.Id, result.ProfileId);
        Assert.Equal(ResolutionSource.Schedule, result.Source);
        Assert.Null(resolver.GetManualOverride(new[] { manual, work }));
    }

    [Fact]
    public void ExpiredOverride_ReturnsToHighestPriorityEvent()
    {
        var clock = new TestClock();
        var resolver = new ProfileResolver(clock);
        var manual = new WallpaperProfile();
        var triggered = new WallpaperProfile();
        var trigger = new EventTrigger { Type = TriggerType.OnBattery, Priority = 50 };
        resolver.SetManual(manual.Id, TimeSpan.FromMinutes(15));
        clock.Advance(TimeSpan.FromMinutes(15));
        var result = resolver.Resolve(clock.GetLocalNow().DateTime, new[] { manual, triggered },
            new[] { new ActiveEventSnapshot(triggered.Id, trigger, 50, clock.GetUtcNow().UtcDateTime) });
        Assert.Equal(triggered.Id, result.ProfileId);
        Assert.Equal(ResolutionSource.Event, result.Source);
    }

    [Fact]
    public void DeletedOverrideProfile_FallsBackImmediately()
    {
        var clock = new TestClock();
        var resolver = new ProfileResolver(clock);
        var fallback = new WallpaperProfile();
        resolver.SetManual(Guid.NewGuid(), TimeSpan.FromMinutes(30));
        Assert.Equal(fallback.Id, resolver.Resolve(clock.GetLocalNow().DateTime, new[] { fallback }, Array.Empty<ActiveEventSnapshot>(), fallback.Id).ProfileId);
        Assert.Null(resolver.GetManualOverride(new[] { fallback }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public void InvalidOverrideDuration_IsRejected(int minutes)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ProfileResolver().SetManual(Guid.NewGuid(), TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void EmptyScheduleWindow_DoesNotCreateAnUpcomingCheckpoint()
    {
        var profile = new WallpaperProfile { Schedule = new() { DailyRule(9, 0, 9, 0) } };
        Assert.Null(ProfileResolver.NextBoundary(new DateTime(2026, 10, 1, 8, 0, 0), new[] { profile }));
    }

    [Theory]
    [InlineData(27, 23, 28, 0)]
    [InlineData(28, 23, 29, 0)]
    [InlineData(28, 5, 28, 6)]
    public void OvernightCheckpoint_AgreesWithDayBasedScheduleRules(int day, int hour, int nextDay, int nextHour)
    {
        var profile = new WallpaperProfile { Schedule = new() { new ScheduleRule
        {
            DaysOfWeek = new() { DayOfWeek.Monday }, StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0)
        } } };
        Assert.Equal(new DateTime(2026, 9, nextDay, nextHour, 0, 0),
            ProfileResolver.NextBoundary(new DateTime(2026, 9, day, hour, 0, 0), new[] { profile }));
    }

    [Fact]
    public void Coordinator_TemporaryChoice_PreservesDefaultAndReturnsWithoutMatchingRules()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Coordinator.SwitchManually(fixture.Temporary.Id, TimeSpan.FromMinutes(15));
        var active = fixture.Coordinator.GetActivitySnapshot();
        Assert.Equal(fixture.Temporary.Id, active.ActiveProfile?.Id);
        Assert.True(active.ManualOverride?.IsTimed);
        Assert.Equal(fixture.Default.Id.ToString(), fixture.SettingsStore.Load().PreferredProfileId);
        Assert.Equal(fixture.Default.Id.ToString(), fixture.SettingsStore.Load().LastActiveProfileId);
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        fixture.Coordinator.CheckNow();
        var automatic = fixture.Coordinator.GetActivitySnapshot();
        Assert.Equal(fixture.Default.Id, automatic.ActiveProfile?.Id);
        Assert.Null(automatic.ManualOverride);
        Assert.Contains("default", automatic.Reason);
        Assert.Contains(automatic.Recent, entry => entry.Reason == "Manual override ended");
    }

    [Fact]
    public void Pause_BlocksAutomaticChanges_WhileAllowingManualChoices()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Coordinator.TogglePaused();
        fixture.Clock.Advance(TimeSpan.FromMinutes(65));
        fixture.Coordinator.CheckNow();
        Assert.Equal(fixture.Default.Id, fixture.Coordinator.ActiveProfileId);
        fixture.Coordinator.SwitchManually(fixture.Temporary.Id, TimeSpan.FromMinutes(15));
        Assert.Equal(fixture.Temporary.Id, fixture.Coordinator.ActiveProfileId);
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        fixture.Coordinator.CheckNow();
        Assert.Null(fixture.Coordinator.GetActivitySnapshot().ManualOverride);
        Assert.Equal(fixture.Temporary.Id, fixture.Coordinator.ActiveProfileId);
        fixture.Coordinator.ResumeAutomatic();
        Assert.False(fixture.Coordinator.Paused);
        Assert.Equal(fixture.Work.Id, fixture.Coordinator.ActiveProfileId);
    }

    [Fact]
    public void ActivitySnapshot_DescribesUpcomingSchedule_AndBoundsSessionHistory()
    {
        using var fixture = new CoordinatorFixture();
        var snapshot = fixture.Coordinator.GetActivitySnapshot();
        Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0), snapshot.NextScheduleAtLocal);
        Assert.Equal("Work", snapshot.NextScheduledProfile);
        for (var i = 0; i < 30; i++) fixture.Coordinator.SwitchManually(i % 2 == 0 ? fixture.Temporary.Id : fixture.Default.Id);
        Assert.Equal(20, fixture.Coordinator.GetActivitySnapshot().Recent.Count);
    }

    [Fact]
    public void SceneAccent_SwitchAndStop_RestoreOriginalTheme()
    {
        var keys = new[] { "SceneAccentColor", "SceneAccentHoverColor", "SceneAccentPressedColor", "SceneAccentSubtleColor" };
        var resources = new ResourceDictionary();
        for (var i = 0; i < keys.Length; i++) resources[keys[i]] = System.Windows.Media.Color.FromRgb((byte)(100 + i), 110, 120);
        var original = keys.Select(key => resources[key]).ToArray();
        using var scene = new SceneController(resources);
        scene.Apply(ScenePresets.Create("Focus"));
        Assert.True(SceneController.TryParseAccent("#B7CDBC", out var focus));
        Assert.Equal(focus, resources[keys[0]]);
        scene.Apply(ScenePresets.Create("Gaming"));
        Assert.NotEqual(focus, resources[keys[0]]);
        scene.Apply(new WallpaperProfile());
        Assert.Equal(original, keys.Select(key => resources[key]).ToArray());
        scene.Apply(ScenePresets.Create("Evening"));
        scene.Dispose();
        Assert.Equal(original, keys.Select(key => resources[key]).ToArray());
    }

    [Theory]
    [InlineData("#12zz34")]
    [InlineData("#123")]
    [InlineData("red")]
    [InlineData("#80112233")]
    public void SceneAccent_RejectsInvalidInput(string input) => Assert.False(SceneController.TryParseAccent(input, out _));

    [Fact]
    public void MissingAmbientTrack_DoesNotBreakSceneActivation()
    {
        using var scene = new SceneController(new ResourceDictionary());
        scene.Apply(new WallpaperProfile { AmbientAudioPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3") });
        Assert.False(scene.HasAudio);
        Assert.Contains("missing", scene.AudioStatus);
        scene.Apply(new WallpaperProfile());
        Assert.Equal("No ambient track", scene.AudioStatus);
    }

    [Fact]
    public void SceneSettings_RoundTrip_AndLegacyProfilesKeepDefaults()
    {
        using var fixture = new CoordinatorFixture();
        var scene = ScenePresets.Create("Evening");
        scene.AmbientAudioPath = @"C:\Audio\rain.wav";
        fixture.Profiles.Save(scene);
        var loaded = fixture.Profiles.LoadAll().Single(p => p.Id == scene.Id);
        Assert.Equal(scene.SceneAccent, loaded.SceneAccent);
        Assert.Equal(scene.AmbientAudioPath, loaded.AmbientAudioPath);
        Assert.Equal(35, loaded.AmbientVolume);
        var legacyId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(fixture.ProfileDirectory, legacyId + ".json"),
            "{\"Id\":\"" + legacyId + "\",\"Name\":\"Legacy\"}");
        var legacy = fixture.Profiles.LoadAll().Single(p => p.Id == legacyId);
        Assert.Equal("", legacy.SceneAccent);
        Assert.Equal("", legacy.AmbientAudioPath);
        Assert.Equal(30, legacy.AmbientVolume);
        Assert.False(legacy.AmbientMuted);
    }

    [Fact]
    public void CanvasBoard_HoldsItsWallpaperUntilAnotherBoardOrManualProfileIsChosen()
    {
        using var fixture = new CoordinatorFixture();
        var directory = Path.GetDirectoryName(fixture.ProfileDirectory)!;
        var wallpaper = Path.Combine(directory, "wallpaper.png"); File.WriteAllText(wallpaper, "test image");
        var canvas = new DesktopCanvasStore(Path.Combine(directory, "canvas.json"));
        var chill = new DesktopBoard { Name = "Chill", WallpaperPath = wallpaper, SwitchAt = new TimeOnly(8, 0),
            Scene = new WallpaperProfile { FolderPath = wallpaper, SlideshowIntervalMinutes = 15, SlideshowRandom = true } };
        var dark = DesktopCanvasFeatures.CopyBoard(chill, "Dark"); dark.SwitchAt = TimeOnly.MinValue;
        canvas.Save(new DesktopCanvasDocument { Enabled = true, Boards = new() { chill, dark }, ActiveBoardId = chill.Id, AutoSwitchBoards = true });
        fixture.Coordinator.AttachCanvas(canvas);
        Assert.Equal("Active board: Chill", fixture.Coordinator.Summary);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        fixture.Coordinator.CheckNow();
        Assert.Equal("Active board: Chill", fixture.Coordinator.Summary);
        fixture.Coordinator.SwitchManually(fixture.Temporary.Id);
        Assert.Equal(fixture.Temporary.Id, fixture.Coordinator.ActiveProfileId);
        Assert.DoesNotContain("Active board", fixture.Coordinator.Summary);
        canvas.Update(document => document.SwitchBoard(dark.Id, fixture.Clock.GetLocalNow().DateTime));
        typeof(WallpaperCoordinator).GetMethod("RefreshCanvasBoard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(fixture.Coordinator, null);
        Assert.Equal("Active board: Dark", fixture.Coordinator.Summary);
        Assert.Null(fixture.Coordinator.CaptureLiveHandoff());
        Assert.Equal(15, DesktopCanvasFeatures.SceneForBoard(canvas.Load().ActiveBoard!).SlideshowIntervalMinutes);
        Assert.True(DesktopCanvasFeatures.SceneForBoard(canvas.Load().ActiveBoard!).SlideshowRandom);
    }

    [Fact]
    public void CanvasTimedWallpaper_AppliesItsWindowAndReturnsToRotation_OnTheSameBoard()
    {
        using var fixture = new CoordinatorFixture();
        var directory = Path.GetDirectoryName(fixture.ProfileDirectory)!;
        var alternate = Path.Combine(directory, "night.png"); File.WriteAllText(alternate, "test image");
        var canvas = new DesktopCanvasStore(Path.Combine(directory, "timed-canvas.json"));
        var board = new DesktopBoard { Name = "Study", WallpaperPath = directory, WallpaperMode = BoardWallpaperMode.RotatingTimed,
            RotationMinutes = 7, OverrideWallpaperPath = alternate, OverrideStart = new TimeOnly(10, 0), OverrideEnd = new TimeOnly(11, 0), Widgets = new() { new DesktopWidget() } };
        canvas.Save(new DesktopCanvasDocument { Enabled = true, Boards = new() { board }, ActiveBoardId = board.Id });
        fixture.Coordinator.AttachCanvas(canvas);
        Assert.Equal(directory, fixture.AppliedProfiles.Last().FolderPath); Assert.Equal(7, fixture.AppliedProfiles.Last().SlideshowIntervalMinutes);
        var refresh = typeof(WallpaperCoordinator).GetMethod("RefreshCanvasBoard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        fixture.Clock.Advance(TimeSpan.FromHours(1)); refresh.Invoke(fixture.Coordinator, null);
        Assert.Equal(alternate, fixture.AppliedProfiles.Last().FolderPath); Assert.Equal(0, fixture.AppliedProfiles.Last().SlideshowIntervalMinutes);
        fixture.Clock.Advance(TimeSpan.FromHours(1)); refresh.Invoke(fixture.Coordinator, null);
        Assert.Equal(directory, fixture.AppliedProfiles.Last().FolderPath); Assert.Equal(7, fixture.AppliedProfiles.Last().SlideshowIntervalMinutes);
        Assert.Equal(board.Id, canvas.Load().ActiveBoardId); Assert.Single(canvas.Load().Widgets);
    }

    private static ScheduleRule DailyRule(int startHour, int startMinute, int endHour, int endMinute) => new()
    {
        DaysOfWeek = Enum.GetValues<DayOfWeek>().ToHashSet(),
        StartTime = new TimeOnly(startHour, startMinute), EndTime = new TimeOnly(endHour, endMinute)
    };

    private sealed class CoordinatorFixture : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "belie-activity-tests-" + Guid.NewGuid().ToString("N"));
        public string ProfileDirectory => Path.Combine(_dir, "profiles");
        public TestClock Clock { get; } = new();
        public WallpaperProfile Default { get; } = new() { Name = "Default" };
        public WallpaperProfile Work { get; } = new() { Name = "Work", Schedule = new() { DailyRule(10, 0, 11, 0) } };
        public WallpaperProfile Temporary { get; } = new() { Name = "Temporary" };
        public ProfileStore Profiles { get; }
        public SettingsStore SettingsStore { get; }
        public WallpaperCoordinator Coordinator { get; }
        public List<WallpaperProfile> AppliedProfiles { get; } = new();

        public CoordinatorFixture()
        {
            Profiles = new ProfileStore(ProfileDirectory);
            foreach (var profile in new[] { Default, Work, Temporary }) Profiles.Save(profile);
            SettingsStore = new SettingsStore(Path.Combine(_dir, "settings.json"));
            var settings = new AppSettings { PreferredProfileId = Default.Id.ToString(), LastActiveProfileId = Default.Id.ToString() };
            Coordinator = new WallpaperCoordinator(Profiles, SettingsStore, settings, Clock, profile => AppliedProfiles.Add(profile));
            Coordinator.Init();
        }
        public void Dispose() { Coordinator.Dispose(); Directory.Delete(_dir, recursive: true); }
    }
}
