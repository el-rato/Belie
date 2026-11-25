using System.IO;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Workspaces;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private const string Main = @"\\.\DISPLAY1", Side = @"\\.\DISPLAY2";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "belie-workspaces-" + Guid.NewGuid());
    private readonly FakeWindows _windows = new();
    private DateTime _now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class FakeWindow
    {
        public required DesktopWindow Window;
        public bool Visible = true;
        public bool Refuses;
        public bool Stuck;
    }

    private sealed class FakeWindows : IWindowSystem
    {
        public readonly Dictionary<nint, FakeWindow> All = new();
        public List<string> Screens = new() { Main, Side };
        public nint Focused;
        public int HideCalls;
        public Action? Hiding;

        public FakeWindow Add(nint handle, string monitor = Main, nint root = 0, long start = 1)
        {
            var window = new FakeWindow { Window = new DesktopWindow(new WindowIdentity(handle, (int)handle, start, "App"), root == 0 ? handle : root, "Window " + handle, monitor) };
            All[handle] = window;
            return window;
        }
        public IReadOnlyList<DesktopWindow> VisibleWindows() => All.Values.Where(w => w.Visible).Select(w => w.Window).ToArray();
        public IReadOnlyList<string> Monitors() => Screens;
        public bool Exists(WindowIdentity window) => All.TryGetValue((nint)window.Handle, out var found) && found.Window.Identity == window;
        public bool IsVisible(nint window) => All.TryGetValue(window, out var found) && found.Visible;
        public void Show(nint window) { if (All.TryGetValue(window, out var found) && !found.Stuck) found.Visible = true; }
        public void Hide(nint window)
        {
            HideCalls++; Hiding?.Invoke();
            if (All.TryGetValue(window, out var found) && !found.Refuses && !found.Stuck) found.Visible = false;
        }
        public nint Foreground() => Focused;
        public void Focus(nint window) => Focused = window;
        public void FocusDesktop() => Focused = 0;
    }

    private string JournalPath => Path.Combine(_directory, "hidden.json");
    private HiddenWindowJournal Journal => new(JournalPath);
    private WorkspaceManager Create(int count = 5) => new(_windows, Journal, count, () => _now);
    private void Settle() => _now += WorkspaceManager.Settle + TimeSpan.FromMilliseconds(1);
    private nint[] Visible => _windows.All.Where(w => w.Value.Visible).Select(w => w.Key).Order().ToArray();

    [Fact]
    public void SwitchingHidesOneWorkspaceAndShowsTheOther()
    {
        _windows.Add(1); _windows.Add(2);
        using var manager = Create();
        Assert.True(manager.Switch(Main, 2));
        Assert.Empty(Visible);
        Settle(); _windows.Add(3); manager.Sync();
        Assert.Equal(new[] { 2, 1 }, manager.States(Main).Take(2).Select(s => s.WindowCount));
        Assert.True(manager.Switch(Main, 1));
        Assert.Equal(new nint[] { 1, 2 }, Visible);
        Assert.True(manager.Switch(Main, 2));
        Assert.Equal(new nint[] { 3 }, Visible);
        Assert.False(manager.Switch(Main, 2));
        Assert.False(manager.Switch(Main, 6));
        Assert.False(manager.Switch(Main, 0));
        Assert.False(manager.Switch("unknown", 1));
    }

    private sealed class DelayedTransition : IWorkspaceTransition
    {
        private Action? _show, _hide;
        public int Direction;
        public TimeSpan Duration => TimeSpan.FromMilliseconds(900);
        public void Run(string monitor, int direction, IReadOnlyCollection<nint> leaving, IReadOnlyCollection<nint> arriving, Action show, Action hide)
        { Finish(); Direction = direction; _show = show; _hide = hide; }
        public void Finish() { var (show, hide) = (_show, _hide); _show = _hide = null; show?.Invoke(); hide?.Invoke(); }
        public void Arrive() { _show?.Invoke(); _show = null; }
    }

    [Fact]
    public void AnimatedSwitchesKeepWindowsWhereTheyBelongThroughout()
    {
        _windows.Add(1);
        var animation = new DelayedTransition();
        using var manager = new WorkspaceManager(_windows, Journal, 5, () => _now, animation);
        manager.Switch(Main, 2);
        Assert.Equal(1, animation.Direction);
        Assert.Equal(new nint[] { 1 }, Visible); // Still on screen while it slides away.
        Assert.Single(Journal.Load());
        _now += TimeSpan.FromMilliseconds(800); manager.Sync();
        Assert.Equal(1, manager.States(Main)[0].WindowCount);
        animation.Finish();
        Assert.Empty(Visible);
        _now += TimeSpan.FromSeconds(3); _windows.Add(2); manager.Sync();
        manager.Switch(Main, 1);
        Assert.Equal(-1, animation.Direction);
        animation.Arrive();
        Assert.Equal(new nint[] { 1, 2 }, Visible);
        // Switching back before the animation ends must not strand either window.
        manager.Switch(Main, 2); animation.Finish();
        Assert.Equal(new nint[] { 2 }, Visible);
        manager.Switch(Main, 1);
        manager.Dispose();
        Assert.Equal(new nint[] { 1, 2 }, Visible);
        Assert.Empty(Journal.Load());
    }

    [Fact]
    public void AWindowReclaimedDuringAnAnimationIsNotHidden()
    {
        _windows.Add(1); _windows.Focused = 1;
        var animation = new DelayedTransition();
        using var manager = new WorkspaceManager(_windows, Journal, 5, () => _now, animation);
        manager.Switch(Main, 5);
        manager.SetCount(1);
        animation.Finish();
        Assert.Equal(new nint[] { 1 }, Visible);
        Assert.Empty(Journal.Load());
    }

    [Fact]
    public void RapidSwitchingKeepsEveryWindowOnItsWorkspace()
    {
        _windows.Add(1);
        using var manager = Create();
        manager.Switch(Main, 2); Settle(); _windows.Add(2); manager.Sync();
        for (var i = 0; i < 20; i++) { manager.Switch(Main, 1 + i % 3); _now += TimeSpan.FromMilliseconds(30); }
        Settle();
        manager.Switch(Main, 1); Settle(); manager.Sync();
        Assert.Equal(new nint[] { 1 }, Visible);
        manager.Switch(Main, 2);
        Assert.Equal(new nint[] { 2 }, Visible);
    }

    [Fact]
    public void AWindowThatIsSlowToReactIsNotMovedWhileItsRequestIsPending()
    {
        var slow = _windows.Add(1);
        using var manager = Create();
        manager.Sync();
        slow.Stuck = true;
        manager.Switch(Main, 2); manager.Sync();
        Assert.Equal(1, manager.States(Main)[0].WindowCount);
        slow.Stuck = false; slow.Visible = false; Settle(); manager.Sync();
        Assert.Equal(1, manager.States(Main)[0].WindowCount);
        slow.Stuck = true; manager.Switch(Main, 1); manager.Sync();
        Assert.Equal(1, manager.States(Main)[0].WindowCount);
    }

    [Fact]
    public void AWindowThatCannotBeHiddenOrShowsItselfJoinsTheOpenWorkspace()
    {
        _windows.Add(1).Refuses = true; var app = _windows.Add(2);
        using var manager = Create();
        manager.Switch(Main, 2); Settle(); manager.Sync();
        Assert.Equal(new[] { 1, 1 }, manager.States(Main).Take(2).Select(s => s.WindowCount));
        app.Visible = true; manager.Sync();
        Assert.Equal(new[] { 0, 2 }, manager.States(Main).Take(2).Select(s => s.WindowCount));
        Assert.Empty(Journal.Load());
    }

    [Fact]
    public void MonitorsAreIndependentAndDraggedWindowsFollowTheirMonitor()
    {
        _windows.Add(1); var moved = _windows.Add(2, Side);
        using var manager = Create();
        manager.Switch(Main, 3);
        Assert.Equal(new nint[] { 2 }, Visible);
        Assert.Equal(1, manager.Active(Side));
        Settle();
        moved.Window = moved.Window with { Monitor = Main }; manager.Sync();
        Assert.Equal(1, manager.States(Main)[2].WindowCount);
        manager.Switch(Main, 1);
        Assert.Equal(new nint[] { 1 }, Visible);
    }

    [Fact]
    public void UnpluggingAMonitorKeepsItsWindowsReachable()
    {
        _windows.Add(1, Side);
        using var manager = Create();
        manager.Switch(Side, 2); Settle(); _windows.Add(2, Side); manager.Sync();
        _windows.Screens = new() { Main };
        _windows.All[2].Window = _windows.All[2].Window with { Monitor = Main };
        _windows.All[1].Window = _windows.All[1].Window with { Monitor = Main };
        manager.Sync();
        Assert.Equal(new nint[] { 1, 2 }, Visible);
        _windows.Screens = new(); manager.Sync();
        Assert.Equal(1, manager.Active(Main));
        Assert.Equal(2, manager.States(Main)[0].WindowCount);
    }

    [Fact]
    public void DialogsTravelWithTheirOwnerAndAreNotCounted()
    {
        _windows.Add(1); _windows.Add(2, root: 1); _windows.Add(3);
        _windows.Focused = 2;
        using var manager = Create();
        manager.Sync();
        Assert.Equal(2, manager.States(Main)[0].WindowCount);
        Assert.True(manager.Move(manager.ActiveWindow, 4));
        Assert.Equal(new nint[] { 3 }, Visible);
        Assert.Equal(1, manager.States(Main)[3].WindowCount);
        manager.Switch(Main, 4);
        Assert.Equal(new nint[] { 1, 2 }, Visible);
        Assert.Equal(2, _windows.Focused);
    }

    [Fact]
    public void FocusReturnsToTheWindowLastUsedOnEachWorkspace()
    {
        _windows.Add(1); _windows.Add(2); _windows.Focused = 2;
        using var manager = Create();
        manager.Sync();
        Assert.Equal(Main, manager.ActiveMonitor);
        manager.Switch(Main, 2);
        Assert.Equal(0, _windows.Focused);
        Assert.Null(manager.ActiveMonitor);
        Settle(); manager.Switch(Main, 1);
        Assert.Equal(2, _windows.Focused);
    }

    [Fact]
    public void CyclingWrapsAround()
    {
        _windows.Add(1);
        using var manager = Create(3);
        manager.Cycle(Main, -1); Assert.Equal(3, manager.Active(Main));
        manager.Cycle(Main, 1); Assert.Equal(1, manager.Active(Main));
    }

    [Fact]
    public void ReducingTheCountMovesWindowsIntoTheLastWorkspace()
    {
        _windows.Add(1); _windows.Focused = 1;
        using var manager = Create(5);
        manager.Sync();
        manager.Move(1, 5);
        manager.SetCount(2);
        Assert.Empty(Visible);
        Assert.Equal(1, manager.States(Main)[1].WindowCount);
        manager.SetCount(1);
        Assert.Equal(new nint[] { 1 }, Visible);
        Assert.Empty(Journal.Load());
    }

    [Fact]
    public void HiddenWindowsAreRecordedFirstAndRestoredOnExit()
    {
        _windows.Add(1);
        var recorded = -1;
        _windows.Hiding = () => recorded = Journal.Load().Count;
        var manager = Create();
        manager.Switch(Main, 2);
        Assert.Equal(1, recorded);
        manager.Dispose();
        Assert.Equal(new nint[] { 1 }, Visible);
        Assert.Empty(Journal.Load());
    }

    [Fact]
    public void ACrashIsRecoveredWithoutTouchingReusedHandles()
    {
        _windows.Add(1); _windows.Add(2); _windows.Add(3);
        var crashed = Create();
        crashed.Switch(Main, 2);
        _windows.Add(2, start: 99).Visible = false; // Same handle, different process.
        _windows.All.Remove(3);
        Assert.Equal(1, Journal.RestoreOrphans(_windows));
        Assert.Equal(new nint[] { 1 }, Visible);
        Assert.Empty(Journal.Load());
    }

    [Fact]
    public void NothingIsHiddenWhenItCannotBeRecorded()
    {
        _windows.Add(1);
        Directory.CreateDirectory(JournalPath); // Makes the file impossible to write.
        using var manager = Create();
        Assert.False(manager.Switch(Main, 2));
        Assert.Equal(1, manager.Active(Main));
        Assert.Equal(0, _windows.HideCalls);
    }

    [Fact]
    public void ADamagedRecordDoesNotStopTheBar()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(JournalPath, "{ not json");
        using var manager = Create();
        Assert.Empty(Journal.Load());
    }

    [Fact]
    public void SettingsRoundTripAndRejectUnusableValues()
    {
        var store = new TopBarStore(Path.Combine(_directory, "topbar.json"));
        Assert.False(store.Load().Enabled);
        var profile = Guid.NewGuid();
        store.Save(new TopBarSettings
        {
            Enabled = true, Height = 900, WorkspaceCount = 40, Opacity = double.NaN, Style = (TopBarStyle)12,
            PinnedApps = new() { " Spotify.exe ", "spotify", @"C:\Apps\Discord.exe", "" },
            WorkspaceProfiles = new() { [2] = profile, [3] = Guid.Empty, [44] = profile }
        });
        var saved = store.Load();
        Assert.True(saved.Enabled);
        Assert.Equal(64, saved.Height); Assert.Equal(9, saved.WorkspaceCount);
        Assert.Equal(.92, saved.Opacity); Assert.Equal(TopBarStyle.Islands, saved.Style);
        Assert.Equal(new[] { "Spotify", "Discord" }, saved.PinnedApps);
        Assert.Equal(profile, Assert.Single(saved.WorkspaceProfiles).Value);
        File.WriteAllText(store.FilePath, "broken");
        Assert.False(store.Load().Enabled);
    }

    [Theory]
    [InlineData(50, 100, 50)]
    [InlineData(0, 100, 100)]
    [InlineData(100, 100, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(120, 100, 0)]
    public void ProcessorUseIsAPercentage(long idle, long total, int expected) => Assert.Equal(expected, SystemReadings.Percent(idle, total));

    [Fact]
    public void GuardIgnoresOtherCommandLines()
    {
        Assert.False(TopBarProcess.RunGuard(Array.Empty<string>()));
        Assert.False(TopBarProcess.RunGuard(new[] { "--minimized", "1", "file" }));
        Assert.True(TopBarProcess.RunGuard(new[] { TopBarProcess.GuardArgument, "not a number", "file" }));
    }
}
