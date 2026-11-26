using System.Drawing;
using System.IO;
using System.Windows.Input;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using WallpaperProfiles.Workspaces;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class TilingTests
{
    private const string Main = @"\\.\DISPLAY1", Side = @"\\.\DISPLAY2";
    private static readonly Rectangle Screen = new(0, 40, 1920, 1000);

    private sealed class FakeTiles : ITileSystem
    {
        public readonly Dictionary<nint, TileWindow> All = new();
        public readonly HashSet<nint> Denied = new();
        public readonly List<nint> Placed = new(), Restored = new(), Maximized = new();
        public nint MinimumWidthWindow;
        /// <summary>Sizes a window will not grow past, without saying so.</summary>
        public readonly Dictionary<nint, Size> Largest = new();
        public void Add(nint window, bool canTile = true, bool maximized = false, bool fixedSize = false)
            => All[window] = new TileWindow(canTile, false, maximized, new Rectangle(100, 100, 500 + (int)window, 400), FixedSize: fixedSize);
        public TileWindow Describe(nint window) => All.GetValueOrDefault(window);
        public readonly Dictionary<nint, Size> Minimums = new();
        public int Asked;
        public Size MinimumSize(nint window) { Asked++; return Minimums.GetValueOrDefault(window); }
        public Rectangle WorkArea(string monitor) => monitor == Main ? Screen : Screen with { X = 1920 };
        public bool Place(nint window, Rectangle frame)
        {
            if (Denied.Contains(window)) return false;
            Placed.Add(window);
            if (window == MinimumWidthWindow) frame.Width = Math.Max(frame.Width, 1500);
            if (Largest.TryGetValue(window, out var largest)) frame.Size = new Size(Math.Min(frame.Width, largest.Width), Math.Min(frame.Height, largest.Height));
            All[window] = All[window] with { Frame = frame };
            return true;
        }
        public void Restore(nint window) { Restored.Add(window); All[window] = All[window] with { Maximized = false }; }
        public void Maximize(nint window) { Maximized.Add(window); All[window] = All[window] with { Maximized = true }; }
    }

    private readonly FakeTiles _tiles = new();
    private readonly List<WorkspaceWindow> _windows = new();
    private void Open(nint window, string monitor = Main, int index = 1, bool hidden = false) => _windows.Add(new WorkspaceWindow(window, monitor, index, hidden));
    private IReadOnlyList<TileMove> Run(TilingEngine engine, nint focused = 0)
    {
        var moves = engine.Update(_windows, focused);
        engine.Apply(moves);
        return moves;
    }

    [Fact]
    public void TilesFillTheAreaWithoutOverlapping()
    {
        for (var count = 1; count <= 8; count++)
        {
            var tree = new DwindleTree();
            for (nint window = 1; window <= count; window++) tree.Insert(window, window - 1);
            var tiles = tree.Layout(Screen, 6).Values.ToArray();
            Assert.Equal(count, tiles.Length);
            Assert.All(tiles, tile => Assert.True(Screen.Contains(tile)));
            for (var i = 0; i < count; i++) for (var j = i + 1; j < count; j++) Assert.False(tiles[i].IntersectsWith(tiles[j]));
            Assert.Equal(Screen, tiles.Aggregate(Rectangle.Union));
        }
    }

    [Fact]
    public void NewWindowsSplitTheTileBesideThemAndClosedOnesGiveTheirSpaceBack()
    {
        var tree = new DwindleTree();
        tree.Insert(1, 0); tree.Insert(2, 1); tree.Insert(3, 1);
        var tiles = tree.Layout(Screen, 0);
        Assert.Equal(new Rectangle(0, 40, 960, 500), tiles[1]);
        Assert.Equal(new Rectangle(0, 540, 960, 500), tiles[3]);
        Assert.Equal(new Rectangle(960, 40, 960, 1000), tiles[2]);
        Assert.Equal((nint)2, tree.Neighbor(1, TileDirection.Right));
        Assert.Equal((nint)3, tree.Neighbor(1, TileDirection.Down));
        Assert.Equal((nint)0, tree.Neighbor(1, TileDirection.Left));
        Assert.True(tree.Swap(1, 2));
        Assert.Equal(new Rectangle(960, 40, 960, 1000), tree.Layout(Screen, 0)[1]);
        // The two left behind get the whole wide screen, so they turn side by side.
        Assert.True(tree.Remove(1));
        Assert.Equal(new Rectangle(0, 40, 960, 1000), tree.Layout(Screen, 0)[2]);
        Assert.True(tree.ToggleSplit(2));
        Assert.Equal(new Rectangle(0, 40, 1920, 500), tree.Layout(Screen, 0)[2]);
        Assert.True(tree.ToggleSplit(2));
        Assert.Equal(new Rectangle(0, 40, 960, 1000), tree.Layout(Screen, 0)[2]);
        Assert.True(tree.Remove(2)); Assert.True(tree.Remove(3)); Assert.False(tree.Remove(3));
        Assert.Empty(tree.Layout(Screen, 0));
        Assert.False(tree.ToggleSplit(3));
    }

    [Fact]
    public void DraggingAnEdgeMovesTheNeighbor()
    {
        var tree = new DwindleTree();
        tree.Insert(1, 0); tree.Insert(2, 1);
        tree.Layout(Screen, 0);
        Assert.True(tree.Resize(1, new Rectangle(0, 40, 1200, 1000)));
        Assert.Equal(new Rectangle(1200, 40, 720, 1000), tree.Layout(Screen, 0)[2]);
        Assert.True(tree.Resize(2, new Rectangle(600, 40, 1320, 1000)));
        Assert.Equal(600, tree.Layout(Screen, 0)[1].Width);
        Assert.True(tree.Resize(1, new Rectangle(0, 40, 5000, 1000)));
        Assert.Equal(192, tree.Layout(Screen, 0)[2].Width);
        Assert.False(tree.Resize(1, tree.Layout(Screen, 0)[1]));
    }

    [Fact]
    public void ANewTileTakesTheChosenShareAndALoneWindowCanFillTheScreen()
    {
        _tiles.Add(1); Open(1);
        var engine = new TilingEngine(_tiles) { Gap = 0, Margin = 20, SplitRatio = .6, SmartGaps = true };
        Run(engine);
        Assert.Equal(Screen, _tiles.All[1].Frame);
        _tiles.Add(2); Open(2);
        Run(engine, 1);
        Assert.Equal(new Rectangle(20, 60, 1128, 960), _tiles.All[1].Frame);
        Assert.Equal(new Rectangle(1148, 60, 752, 960), _tiles.All[2].Frame);
    }

    [Fact]
    public void WindowsAreTiledOnceAndLeftAloneAfterThat()
    {
        _tiles.Add(1); _tiles.Add(2); Open(1); Open(2);
        var engine = new TilingEngine(_tiles) { Gap = 10, Margin = 10 };
        var moves = Run(engine);
        Assert.All(moves, move => Assert.True(move.IsNew));
        Assert.Equal(new Rectangle(10, 50, 945, 980), _tiles.All[1].Frame);
        Assert.Equal(new Rectangle(965, 50, 945, 980), _tiles.All[2].Frame);
        Assert.Empty(Run(engine));
        _windows.RemoveAt(0);
        Assert.Equal(new Rectangle(10, 50, 1900, 980), Assert.Single(Run(engine)).To);
    }

    [Fact]
    public void ANewFocusedWindowSplitsTheTileFocusedBeforeIt()
    {
        _tiles.Add(1); _tiles.Add(2); _tiles.Add(3); Open(1); Open(2);
        var engine = new TilingEngine(_tiles) { Gap = 0, Margin = 0 };
        Run(engine); Run(engine, focused: 1);
        Open(3);
        Run(engine, focused: 3);
        Assert.Equal(new Rectangle(0, 540, 960, 500), _tiles.All[3].Frame);
        Assert.Equal(new Rectangle(960, 40, 960, 1000), _tiles.All[2].Frame);
    }

    [Fact]
    public void FixedSizeMinimizedAndDeniedWindowsTakeNoSpace()
    {
        _tiles.Add(1); _tiles.Add(2, canTile: false); _tiles.Add(3); _tiles.Add(4);
        _tiles.All[3] = _tiles.All[3] with { Minimized = true };
        _tiles.Denied.Add(4);
        foreach (nint window in new[] { 1, 2, 3, 4 }) Open(window);
        var engine = new TilingEngine(_tiles) { Gap = 0, Margin = 0 };
        Run(engine); Run(engine);
        Assert.Equal(Screen, _tiles.All[1].Frame);
        Assert.False(engine.IsTiled(2)); Assert.False(engine.IsTiled(3)); Assert.False(engine.IsTiled(4));
        _tiles.All[3] = _tiles.All[3] with { Minimized = false };
        Run(engine);
        Assert.True(engine.IsTiled(3));
        Assert.Equal(960, _tiles.All[1].Frame.Width);
    }

    [Fact]
    public void MaximizedWindowsAreTiledAtFirstButStayMaximizedWhenTheUserAsks()
    {
        _tiles.Add(1, maximized: true); _tiles.Add(2); Open(1); Open(2);
        var engine = new TilingEngine(_tiles) { Gap = 0, Margin = 0 };
        Run(engine);
        Assert.Equal((nint)1, Assert.Single(_tiles.Restored));
        _tiles.Maximize(1); _tiles.Placed.Clear();
        Assert.Empty(Run(engine));
        _tiles.All[1] = _tiles.All[1] with { Maximized = false };
        Run(engine);
        Assert.Equal((nint)1, Assert.Single(_tiles.Placed));
        Assert.Single(_tiles.Restored);
    }

    [Fact]
    public void AnAppThatMovesItselfIsCorrectedOnlyAFewTimes()
    {
        _tiles.Add(1); _tiles.Add(2); Open(1); Open(2);
        long time = 0;
        var engine = new TilingEngine(_tiles, () => time) { Gap = 0, Margin = 0 };
        Run(engine);
        for (var i = 0; i < 3; i++)
        {
            _tiles.All[2] = _tiles.All[2] with { Frame = new Rectangle(10, 60, 500, 400) };
            Assert.Empty(Run(engine)); // It may still be on its way.
            time += 300;
            Assert.True(Assert.Single(Run(engine)).IsCorrection);
        }
        _tiles.All[2] = _tiles.All[2] with { Frame = new Rectangle(10, 60, 500, 400) };
        time += 300;
        Assert.Empty(Run(engine));
    }

    [Fact]
    public void NeighborsMakeRoomForAnAppThatCannotBeSmall()
    {
        _tiles.Add(1); _tiles.Add(2); Open(1); Open(2); _tiles.MinimumWidthWindow = 2; // Never says so; it just stays large.
        long time = 0;
        var engine = new TilingEngine(_tiles, () => time) { Gap = 0, Margin = 0 };
        Run(engine);
        time += 300;
        Assert.Equal(2, Run(engine).Count);
        Assert.Equal(420, _tiles.All[1].Frame.Width);
        Assert.Equal(new Rectangle(420, 40, 1500, 1000), _tiles.All[2].Frame);
        time += 300;
        Assert.Empty(Run(engine));
    }

    [Fact]
    public void MinimumSizesDecideTheSplitAndKeepTilesOnTheScreen()
    {
        foreach (var window in new nint[] { 1, 2, 3 }) { _tiles.Add(window); Open(window); _tiles.Minimums[window] = new Size(627, 300); }
        var engine = new TilingEngine(_tiles) { Gap = 0, Margin = 0 };
        Run(engine, 2);
        // Half the screen is wider than tall but too narrow for two of these, so the third opens below.
        Assert.Equal(new Rectangle(0, 40, 960, 1000), _tiles.All[1].Frame);
        Assert.Equal(new Rectangle(960, 40, 960, 500), _tiles.All[2].Frame);
        Assert.Equal(new Rectangle(960, 540, 960, 500), _tiles.All[3].Frame);

        // More than can ever fit still stays on the screen, at sizes the apps accept.
        foreach (var window in Enumerable.Range(4, 8).Select(index => (nint)index)) { _tiles.Add(window); Open(window); _tiles.Minimums[window] = new Size(900, 700); }
        Run(engine, 3);
        Assert.All(_tiles.All.Values, tile => Assert.True(Screen.Contains(tile.Frame)));
        Assert.All(Enumerable.Range(4, 8), index => Assert.True(_tiles.All[index].Frame is { Width: >= 900, Height: >= 700 }));
    }

    [Fact]
    public void AnAppWithoutAMinimumIsAskedLessAndLessOften()
    {
        _tiles.Add(1); _tiles.Add(2); Open(1); Open(2);
        long time = 0;
        var engine = new TilingEngine(_tiles, () => time) { Gap = 0, Margin = 0 };
        Run(engine);
        var asked = _tiles.Asked;
        for (; time < 600_000; time += 500) Run(engine);
        Assert.InRange(_tiles.Asked - asked, 2, 24);
        // A late answer is still noticed.
        _tiles.Minimums[2] = new Size(1200, 300);
        time += 130_000;
        Run(engine);
        Assert.Equal(1200, _tiles.All[2].Frame.Width);
    }

    [Fact]
    public void NeighborsFollowAnEdgeWhileItIsDragged()
    {
        _tiles.Add(1); _tiles.Add(2); Open(1); Open(2);
        var engine = new TilingEngine(_tiles) { Gap = 10, Margin = 0 };
        Run(engine);
        _tiles.Placed.Clear();
        _tiles.All[1] = _tiles.All[1] with { Frame = new Rectangle(300, 200, 955, 1000) }; // Moved, not resized.
        engine.UserResizing(1);
        Assert.Empty(_tiles.Placed);
        foreach (var width in new[] { 1000, 1100, 1200 })
        {
            _tiles.All[1] = _tiles.All[1] with { Frame = new Rectangle(0, 40, width, 1000) };
            engine.UserResizing(1);
            Assert.Equal(new Rectangle(width + 10, 40, 1910 - width, 1000), _tiles.All[2].Frame);
        }
        Assert.DoesNotContain((nint)1, _tiles.Placed); // Never fights the pointer.
        engine.UserMoved(1, new Point(1200, 300));
        Run(engine);
        Assert.Equal(new Rectangle(0, 40, 1200, 1000), _tiles.All[1].Frame);
        Assert.Equal(1210, _tiles.All[2].Frame.X);
    }

    [Fact]
    public void WorkspacesAndMonitorsKeepSeparateLayouts()
    {
        _tiles.Add(1); _tiles.Add(2); _tiles.Add(3); Open(1); Open(2, Side); Open(3);
        var engine = new TilingEngine(_tiles) { Gap = 0, Margin = 0 };
        Run(engine);
        Assert.Equal(Screen with { X = 1920 }, _tiles.All[2].Frame);
        _windows[2] = new WorkspaceWindow(3, Main, 2, true);
        Assert.Contains(Run(engine), move => move.Window == 3 && move.Hidden);
        Assert.Equal(Screen, _tiles.All[1].Frame);
        Assert.Equal(Screen, _tiles.All[3].Frame);
    }

    [Fact]
    public void DraggingSwapsFloatingLeavesAndReleaseRestores()
    {
        _tiles.Add(1); _tiles.Add(2); _tiles.Add(3, maximized: true); Open(1); Open(2);
        var first = _tiles.All[1].Frame;
        var engine = new TilingEngine(_tiles) { Gap = 0, Margin = 0 };
        Run(engine);
        _tiles.All[1] = _tiles.All[1] with { Frame = _tiles.All[1].Frame with { X = 800 } };
        engine.UserMoved(1, new Point(1500, 300)); Run(engine);
        Assert.Equal(960, _tiles.All[1].Frame.X); Assert.Equal(0, _tiles.All[2].Frame.X);
        Assert.True(engine.Swap(1, TileDirection.Left)); Run(engine);
        Assert.Equal(0, _tiles.All[1].Frame.X);
        _tiles.All[1] = _tiles.All[1] with { Frame = _tiles.All[1].Frame with { Width = 1200 } };
        engine.UserMoved(1, new Point(1200, 300)); Run(engine);
        Assert.Equal(720, _tiles.All[2].Frame.Width);
        engine.ToggleFloating(1); Run(engine);
        Assert.Equal(first, _tiles.All[1].Frame); Assert.Equal(Screen, _tiles.All[2].Frame);
        engine.ToggleFloating(1); Run(engine);
        Assert.True(engine.IsTiled(1));
        Open(3); Run(engine);
        engine.Release();
        Assert.Equal(first, _tiles.All[1].Frame);
        Assert.Equal((nint)3, Assert.Single(_tiles.Maximized));
    }

    [Fact]
    public void EasingStartsAndEndsExactlyAndOnlyOvershootsWhereIntended()
    {
        foreach (var ease in new Func<double, double>[] { Motion.Slide, Motion.Open, Motion.Fade, Motion.Move })
        {
            Assert.Equal(0, ease(0), 6); Assert.Equal(1, ease(1), 6);
        }
        var steps = Enumerable.Range(0, 101).Select(i => i / 100d).ToArray();
        Assert.True(steps.Zip(steps.Skip(1)).All(pair => Motion.Slide(pair.First) <= Motion.Slide(pair.Second) + 1e-9));
        Assert.True(steps.Max(Motion.Open) > 1.01);
        Assert.True(steps.Max(Motion.Slide) <= 1);
        Assert.InRange(Motion.Spring(.44, 130, 23), .95, 1.05);
        Assert.Equal(1, Motion.Spring(20, 130, 23), 6); Assert.Equal(1, Motion.Spring(20, 100, 20), 6); Assert.Equal(1, Motion.Spring(20, 10, 40), 1);
        Assert.Equal(new Rectangle(50, 10, 150, 100), SceneOverlay.Blend(new(0, 0, 100, 100), new(100, 20, 200, 100), .5));
        Assert.Equal(1, SceneOverlay.Blend(new(0, 0, 10, 10), new(0, 0, 1, 1), 3).Width);
    }

    [Theory]
    [InlineData("Alt+2", "Alt", 2)]
    [InlineData("Alt+6", "Alt", null)]
    [InlineData("Alt+2", "CtrlAlt", null)]
    [InlineData("Ctrl+Alt+5", "CtrlAlt", 5)]
    [InlineData("Alt+2", "None", null)]
    [InlineData("Alt+0", "Alt", null)]
    [InlineData("Alt+Q", "Alt", null)]
    public void RecognizesWorkspaceShortcuts(string keys, string modifier, int? expected) => Check(keys, Enum.Parse<WorkspaceShortcutModifier>(modifier), expected);

    private static void Check(string keys, WorkspaceShortcutModifier modifier, int? expected)
    {
        var gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(keys)!;
        Assert.Equal(expected, new TopBarSettings { WorkspacesEnabled = true, Shortcuts = modifier }.WorkspaceFor(gesture));
        Assert.Null(new TopBarSettings { Enabled = true, Shortcuts = modifier }.WorkspaceFor(gesture));
    }

    [Fact]
    public void AProfileShortcutThatIsAlsoAWorkspaceShortcutOpensTheWorkspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "belie-forward-" + Guid.NewGuid());
        try
        {
            var file = Path.Combine(directory, "topbar.json");
            Guid own = Guid.NewGuid(), linked = Guid.NewGuid();
            new TopBarStore(file).Save(new TopBarSettings { WorkspacesEnabled = true, WorkspaceProfiles = new() { [2] = linked } });
            Assert.Null(TopBarProcess.ForwardShortcut(file, "Alt+2", own)); // No bar is running.
            using var command = new EventWaitHandle(false, EventResetMode.AutoReset, TopBarProcess.MutexName(file) + ".Command");
            Assert.Equal(linked, TopBarProcess.ForwardShortcut(file, "Alt+2", own));
            Assert.True(command.WaitOne(0));
            Assert.Equal(2, AtomicJsonFile.Read<WorkspaceCommand>(TopBarProcess.CommandFile(file))!.Workspace);
            Assert.Equal(own, TopBarProcess.ForwardShortcut(file, "Alt+3", own));
            Assert.Null(TopBarProcess.ForwardShortcut(file, "Ctrl+Alt+2", own));
            Assert.Null(TopBarProcess.ForwardShortcut(file, "", own));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
