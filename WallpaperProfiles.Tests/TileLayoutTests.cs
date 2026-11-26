using System.Drawing;
using WallpaperProfiles.Models;
using WallpaperProfiles.Workspaces;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class TileLayoutTests
{
    private const string Main = @"\\.\DISPLAY1";
    private static readonly Rectangle Wide = new(0, 40, 1920, 1000), Tall = new(0, 40, 1000, 1920);

    private sealed class FakeTiles : ITileSystem
    {
        public readonly Dictionary<nint, TileWindow> All = new();
        public readonly Dictionary<nint, Size> Largest = new();
        public Rectangle Work = Wide;
        public int Placed;
        public void Add(nint window, bool fixedSize = false)
            => All[window] = new TileWindow(true, false, false, new Rectangle(100, 100, 300, 200), FixedSize: fixedSize);
        public TileWindow Describe(nint window) => All.GetValueOrDefault(window);
        public Size MinimumSize(nint window) => Size.Empty;
        public Rectangle WorkArea(string monitor) => Work;
        public bool Place(nint window, Rectangle frame)
        {
            Placed++;
            if (Largest.TryGetValue(window, out var largest)) frame.Size = new Size(Math.Min(frame.Width, largest.Width), Math.Min(frame.Height, largest.Height));
            All[window] = All[window] with { Frame = frame };
            return true;
        }
        public void Restore(nint window) { }
        public void Maximize(nint window) { }
    }

    private static DwindleTree Tree(TileLayout mode, int count)
    {
        var tree = new DwindleTree { Mode = mode };
        for (nint window = 1; window <= count; window++) tree.Insert(window, window - 1);
        return tree;
    }

    private static void AssertFills(Rectangle area, IReadOnlyCollection<Rectangle> tiles)
    {
        var all = tiles.ToArray();
        Assert.All(all, tile => Assert.True(area.Contains(tile), $"{tile} leaves {area}"));
        for (var i = 0; i < all.Length; i++) for (var j = i + 1; j < all.Length; j++) Assert.False(all[i].IntersectsWith(all[j]), $"{all[i]} overlaps {all[j]}");
        Assert.Equal((long)area.Width * area.Height, all.Sum(tile => (long)tile.Width * tile.Height));
    }

    [Theory]
    [InlineData("Dwindle")]
    [InlineData("Grid")]
    [InlineData("Columns")]
    [InlineData("MainAndStack")]
    public void EveryLayoutFillsWideAndTallScreensWithAnyNumberOfWindows(string layout)
    {
        var mode = Enum.Parse<TileLayout>(layout);
        foreach (var area in new[] { Wide, Tall })
            for (var count = 1; count <= 16; count++)
            {
                var tiles = Tree(mode, count).Layout(area, 0).Values.ToArray();
                Assert.Equal(count, tiles.Length);
                AssertFills(area, tiles);
            }
    }

    [Fact]
    public void GapsAreKeptBetweenTilesInEveryLayout()
    {
        foreach (var mode in Enum.GetValues<TileLayout>())
        {
            var tiles = Tree(mode, 7).Layout(Wide, 10).Values.ToArray();
            Assert.All(tiles, tile => Assert.True(Wide.Contains(tile)));
            for (var i = 0; i < tiles.Length; i++)
                for (var j = i + 1; j < tiles.Length; j++)
                {
                    var grown = tiles[i]; grown.Inflate(4, 4);
                    Assert.False(grown.IntersectsWith(tiles[j]), $"{mode}: {tiles[i]} is too close to {tiles[j]}");
                }
        }
    }

    [Fact]
    public void TheGridKeepsTilesInAComfortableShape()
    {
        Assert.Equal(1, DwindleTree.GridColumns(1, Wide));
        Assert.Equal(2, DwindleTree.GridColumns(4, Wide));
        Assert.Equal(3, DwindleTree.GridColumns(6, Wide));
        Assert.Equal(3, DwindleTree.GridColumns(9, Wide));
        Assert.Equal(4, DwindleTree.GridColumns(12, Wide));
        Assert.Equal(new[] { new Rectangle(0, 40, 960, 500), new(0, 540, 960, 500), new(960, 40, 960, 500), new(960, 540, 960, 500) },
            Tree(TileLayout.Grid, 4).Layout(Wide, 0).OrderBy(item => item.Key).Select(item => item.Value));
        // An odd window goes to the last column, so the first one opened keeps a larger tile.
        var five = Tree(TileLayout.Grid, 5).Layout(Wide, 0);
        Assert.Equal(new Rectangle(0, 40, 960, 500), five[1]);
        Assert.Equal(new Rectangle(960, 40, 960, 333), five[3]);
        // A turned monitor gets rows instead of columns.
        var tall = Tree(TileLayout.Grid, 2).Layout(Tall, 0);
        Assert.Equal(new Rectangle(0, 40, 1000, 960), tall[1]);
        Assert.Equal(new Rectangle(0, 1000, 1000, 960), tall[2]);
    }

    [Fact]
    public void ColumnsBecomeRowsOnATallScreen()
    {
        var tree = Tree(TileLayout.Columns, 3);
        Assert.All(tree.Layout(Wide, 0).Values, tile => Assert.Equal(640, tile.Width));
        Assert.All(tree.Layout(Tall, 0).Values, tile => Assert.Equal(1000, tile.Width));
        Assert.Equal(1920, tree.Layout(Tall, 0).Values.Sum(tile => tile.Height));
    }

    [Fact]
    public void DwindleSplitsFollowTheScreenWhenItTurns()
    {
        var tree = Tree(TileLayout.Dwindle, 2);
        Assert.Equal(960, tree.Layout(Wide, 0)[1].Width);
        var turned = tree.Layout(Tall, 0);
        Assert.Equal(new Rectangle(0, 40, 1000, 960), turned[1]);
        Assert.Equal(new Rectangle(0, 1000, 1000, 960), turned[2]);
    }

    [Fact]
    public void TheMainWindowTakesItsShareAndTheStackSharesTheRest()
    {
        var tree = Tree(TileLayout.MainAndStack, 4);
        tree.SplitRatio = .6;
        var tiles = tree.Layout(Wide, 0);
        Assert.Equal(new Rectangle(0, 40, 1152, 1000), tiles[1]);
        Assert.All(new nint[] { 2, 3, 4 }, window => { Assert.Equal(768, tiles[window].Width); Assert.InRange(tiles[window].Height, 333, 334); });
        Assert.Equal(1000, new nint[] { 2, 3, 4 }.Sum(window => tiles[window].Height));
        // Dragging the edge between them moves it.
        Assert.True(tree.Resize(2, new Rectangle(1000, 40, 920, 333)));
        Assert.Equal(1000, tree.Layout(Wide, 0)[1].Width);
        Assert.False(Tree(TileLayout.Grid, 4).ToggleSplit(1));
    }

    [Fact]
    public void NewWindowsOpenBesideTheFocusedOneAndSwapsTradePlaces()
    {
        var tree = Tree(TileLayout.Columns, 3);
        tree.Insert(4, 1); // Beside the first.
        Assert.Equal(480, tree.Layout(Wide, 0)[4].X);
        Assert.True(tree.Swap(4, 3));
        var tiles = tree.Layout(Wide, 0);
        Assert.Equal(480, tiles[3].X); Assert.Equal(1440, tiles[4].X);
        Assert.Equal((nint)1, tree.Neighbor(3, TileDirection.Left));
        Assert.True(tree.Remove(3));
        Assert.Equal(640, tree.Layout(Wide, 0)[2].X);
    }

    [Fact]
    public void SharesGiveEachTileItsMinimumWhenThereIsRoom()
    {
        var (starts, lengths) = DwindleTree.Split(1000, 0, new[] { 600, 0, 0 });
        Assert.Equal(new[] { 0, 600, 800 }, starts); Assert.Equal(new[] { 600, 200, 200 }, lengths);
        (starts, lengths) = DwindleTree.Split(1000, 10, new[] { 0, 0 });
        Assert.Equal(new[] { 0, 505 }, starts); Assert.Equal(new[] { 495, 495 }, lengths);
        // Weighted, then the larger minimum wins.
        Assert.Equal(new[] { 700, 300 }, DwindleTree.Split(1000, 0, new[] { 0, 0 }, new[] { .7, .3 }).Lengths);
        Assert.Equal(new[] { 600, 400 }, DwindleTree.Split(1000, 0, new[] { 0, 400 }, new[] { .7, .3 }).Lengths);
        // No room for everything: shared out evenly and the windows overlap later.
        Assert.Equal(new[] { 500, 500 }, DwindleTree.Split(1000, 0, new[] { 900, 900 }).Lengths);
    }

    [Fact]
    public void MinimumSizesStillDecideTheGrid()
    {
        var tree = Tree(TileLayout.Grid, 4);
        var tiles = tree.Layout(Wide, 0, window => window == 1 ? new Size(1300, 0) : Size.Empty);
        Assert.Equal(1300, tiles[1].Width);
        Assert.Equal(620, tiles[3].Width);
        AssertFills(Wide, tiles.Values.ToArray());
    }

    [Fact]
    public void AWindowThatWillNotGrowIsCenteredInItsTile()
    {
        Rectangle tile = new(0, 40, 960, 1000), area = Wide;
        Assert.Equal(tile, TilingEngine.Fit(tile, area, Size.Empty, Size.Empty));
        Assert.Equal(new Rectangle(230, 240, 500, 600), TilingEngine.Fit(tile, area, Size.Empty, new Size(500, 600)));
        Assert.Equal(new Rectangle(230, 40, 500, 1000), TilingEngine.Fit(tile, area, Size.Empty, new Size(500, 0)));
        // Too large for its tile: it overlaps its neighbor, but stays on the screen.
        Assert.Equal(new Rectangle(0, 40, 1200, 1000), TilingEngine.Fit(tile, area, new Size(1200, 0), Size.Empty));
        Assert.Equal(new Rectangle(1920 - 1200, 40, 1200, 1000), TilingEngine.Fit(tile with { X = 960 }, area, new Size(1200, 0), Size.Empty));
    }

    [Fact]
    public void FixedSizeWindowsAreStretchedAndCenteredWhenTheyRefuse()
    {
        var tiles = new FakeTiles();
        tiles.Add(1); tiles.Add(2, fixedSize: true);
        tiles.Largest[2] = new Size(400, 300); // Ignores being made larger.
        var windows = new List<WorkspaceWindow> { new(1, Main, 1, false), new(2, Main, 1, false) };
        long time = 0;
        var engine = new TilingEngine(tiles, () => time) { Gap = 0, Margin = 0 };
        engine.Apply(engine.Update(windows, 0));
        Assert.True(engine.IsTiled(2));
        Assert.Equal(new Rectangle(960, 40, 400, 300), tiles.All[2].Frame); // It was asked to fill the tile.
        time += 300;
        Assert.True(Assert.Single(engine.Update(windows, 0)).IsCorrection); // It may still be catching up, so it is asked again.
        engine.Apply(engine.Update(windows, 0));
        time += 300;
        engine.Apply(engine.Update(windows, 0));
        Assert.Equal(new Rectangle(960 + 280, 40 + 350, 400, 300), tiles.All[2].Frame);
        Assert.Equal(new Rectangle(0, 40, 960, 1000), tiles.All[1].Frame); // Its neighbor is not disturbed.
        time += 300;
        Assert.Empty(engine.Update(windows, 0)); // Settled, not fought over.

        // A fixed-size window that does stretch simply fills its tile.
        tiles.Largest.Clear();
        tiles.Add(3, fixedSize: true);
        windows.Add(new(3, Main, 1, false));
        engine.Apply(engine.Update(windows, 3));
        Assert.True(engine.IsTiled(3));
        Assert.Equal(tiles.All[3].Frame.Size, new Size(960, 500));
    }

    [Fact]
    public void FixedSizeWindowsCanBeLeftOut()
    {
        var tiles = new FakeTiles();
        tiles.Add(1); tiles.Add(2, fixedSize: true);
        var windows = new List<WorkspaceWindow> { new(1, Main, 1, false), new(2, Main, 1, false) };
        var engine = new TilingEngine(tiles) { Gap = 0, Margin = 0, TileFixedSize = false };
        engine.Apply(engine.Update(windows, 0));
        Assert.False(engine.IsTiled(2));
        Assert.Equal(Wide, tiles.All[1].Frame);
    }

    [Fact]
    public void LayoutsAndScreenSizesChangeWithoutReopeningWindows()
    {
        var tiles = new FakeTiles();
        var windows = new List<WorkspaceWindow>();
        for (nint window = 1; window <= 12; window++) { tiles.Add(window); windows.Add(new(window, Main, 1, false)); }
        var engine = new TilingEngine(tiles) { Gap = 0, Margin = 0, Layout = TileLayout.Grid };
        engine.Apply(engine.Update(windows, 0));
        Assert.All(windows, window => Assert.True(engine.IsTiled(window.Handle)));
        AssertFills(Wide, tiles.All.Values.Select(tile => tile.Frame).ToArray());
        Assert.All(tiles.All.Values, tile => Assert.Equal(480, tile.Frame.Width));

        tiles.Work = Tall;
        engine.Apply(engine.Update(windows, 0));
        AssertFills(Tall, tiles.All.Values.Select(tile => tile.Frame).ToArray());
        Assert.All(tiles.All.Values, tile => Assert.Equal(480, tile.Frame.Height));

        engine.Layout = TileLayout.Columns;
        engine.Apply(engine.Update(windows, 0));
        Assert.All(tiles.All.Values, tile => Assert.Equal(1000, tile.Frame.Width));
    }

    [Theory]
    [InlineData(0x160B0000, true, false)] // Firefox: draws its own frame, keeps a maximize button.
    [InlineData(0x16CF0000, true, false)] // An ordinary resizable window.
    [InlineData(0x16C80000, true, true)]  // A fixed-size window with a title bar.
    [InlineData(unchecked((int)0x96000000), false, true)] // A splash screen.
    public void AppsThatDrawTheirOwnFrameAreStillTiled(int style, bool tiles, bool fixedSize)
        => Assert.Equal((tiles, fixedSize), NativeTileSystem.Shape(style));

    /// <summary>Firefox with no minimum, then Claude and Spotify, which cannot be smaller than these.</summary>
    [Theory]
    [InlineData("Dwindle")]
    [InlineData("Grid")]
    [InlineData("Columns")]
    [InlineData("MainAndStack")]
    public void WindowsTradeTilesSoLargeMinimumsDoNotCrushTheOthers(string layout)
    {
        var minimums = new Dictionary<nint, Size> { [1] = Size.Empty, [2] = new(768, 509), [3] = new(1000, 750) };
        var tiles = new FakeTiles { Work = new Rectangle(6, 54, 1908, 960) };
        var windows = new List<WorkspaceWindow>();
        var engine = new TilingEngine(new MinimumTiles(tiles, minimums)) { Gap = 2, Margin = 0, SplitRatio = .45, Layout = Enum.Parse<TileLayout>(layout) };
        for (nint window = 1; window <= 3; window++)
        {
            tiles.Add(window); windows.Add(new(window, Main, 1, false));
            engine.Apply(engine.Update(windows, window - 1));
        }
        var frames = tiles.All.ToDictionary(item => item.Key, item => item.Value.Frame);
        Assert.All(frames, item => Assert.True(item.Value.Width >= minimums[item.Key].Width && item.Value.Height >= minimums[item.Key].Height, $"{layout}: {item.Key} got {item.Value}"));
        for (nint i = 1; i <= 3; i++) for (nint j = (nint)(i + 1); j <= 3; j++) Assert.False(frames[i].IntersectsWith(frames[j]), $"{layout}: {frames[i]} overlaps {frames[j]}");
        // The window with no minimum is not left a strip, except in Columns, where it can only have what is left.
        Assert.True(Math.Min(frames[1].Width, frames[1].Height) >= (layout == "Columns" ? 136 : 400), $"{layout}: Firefox got {frames[1]}");
    }

    private sealed class MinimumTiles(FakeTiles inner, IReadOnlyDictionary<nint, Size> minimums) : ITileSystem
    {
        public TileWindow Describe(nint window) => inner.Describe(window);
        public Rectangle WorkArea(string monitor) => inner.WorkArea(monitor);
        public Size MinimumSize(nint window) => minimums.GetValueOrDefault(window);
        public bool Place(nint window, Rectangle frame) => inner.Place(window, frame);
        public void Restore(nint window) => inner.Restore(window);
        public void Maximize(nint window) => inner.Maximize(window);
    }

    [Fact]
    public void DraggingNeverTurnsASplit()
    {
        // A 45% first window leaves the other half just wider than tall, so a drag can push it either way.
        foreach (var distance in new[] { 40, 150, 300, -150, -300 })
        {
            var tiles = new FakeTiles();
            var windows = new List<WorkspaceWindow>();
            var engine = new TilingEngine(tiles) { Gap = 2, Margin = 6, SplitRatio = .45 };
            for (nint window = 1; window <= 3; window++)
            {
                tiles.Add(window); windows.Add(new(window, Main, 1, false));
                engine.Apply(engine.Update(windows, window - 1));
            }
            Rectangle two = tiles.All[2].Frame, three = tiles.All[3].Frame;
            var sideBySide = two.Y == three.Y;
            var first = tiles.All[1].Frame;
            var dragTo = first with { Width = first.Width + distance };
            for (var step = 1; step <= 10; step++)
            {
                tiles.All[1] = tiles.All[1] with { Frame = first with { Width = first.Width + distance * step / 10 } };
                engine.UserResizing(1);
                Assert.Equal(sideBySide, tiles.All[2].Frame.Y == tiles.All[3].Frame.Y);
            }
            engine.UserMoved(1, new Point(dragTo.Right, 300));
            engine.Apply(engine.Update(windows, 1));
            Assert.Equal(dragTo.Right, tiles.All[1].Frame.Right);
            Assert.Equal(sideBySide, tiles.All[2].Frame.Y == tiles.All[3].Frame.Y);
            Assert.Equal(dragTo.Right + 2, Math.Min(tiles.All[2].Frame.X, tiles.All[3].Frame.X));
        }
        // Opening or closing a window still lets the splits suit their new shape.
        var tree = Tree(TileLayout.Dwindle, 3);
        tree.Layout(Wide, 0);
        Assert.True(tree.Remove(1));
        Assert.Equal(960, tree.Layout(Wide, 0)[2].Width);
    }

    [Fact]
    public void AWindowThatNeedsTheWholeScreenDoesNotStopTiling()
    {
        var tree = Tree(TileLayout.Dwindle, 2);
        var tiles = tree.Layout(Wide, 10, window => window == 2 ? new Size(1910, 0) : Size.Empty);
        Assert.Equal(2, tiles.Count);
        Assert.All(tiles.Values, tile => Assert.True(Wide.Contains(tile)));
    }

    /// <summary>Drags every edge between two tiles in every layout, as the user would with the mouse.</summary>
    [Theory]
    [InlineData("Dwindle", 3)]
    [InlineData("Dwindle", 5)]
    [InlineData("Grid", 3)]
    [InlineData("Grid", 6)]
    [InlineData("Columns", 4)]
    [InlineData("MainAndStack", 4)]
    public void EveryInnerEdgeCanBeDraggedWithManyWindows(string layout, int count)
    {
        var mode = Enum.Parse<TileLayout>(layout);
        for (nint dragged = 1; dragged <= count; dragged++)
            for (var side = 0; side < 4; side++)
            {
                var tiles = new FakeTiles();
                var windows = new List<WorkspaceWindow>();
                for (nint window = 1; window <= count; window++) { tiles.Add(window); windows.Add(new(window, Main, 1, false)); }
                long time = 0;
                var engine = new TilingEngine(tiles, () => time) { Gap = 6, Margin = 0, Layout = mode };
                for (nint window = 1; window <= count; window++) engine.Apply(engine.Update(windows.Take((int)window).ToList(), window - 1));
                var tile = tiles.All[dragged].Frame;
                // Only edges with a neighbor behind them can move; the screen's edges cannot.
                if (side switch { 0 => tile.Left <= Wide.Left, 1 => tile.Right >= Wide.Right, 2 => tile.Top <= Wide.Top, _ => tile.Bottom >= Wide.Bottom }) continue;
                var dragTo = side switch
                {
                    0 => Rectangle.FromLTRB(tile.Left + 40, tile.Top, tile.Right, tile.Bottom), 1 => Rectangle.FromLTRB(tile.Left, tile.Top, tile.Right - 40, tile.Bottom),
                    2 => Rectangle.FromLTRB(tile.Left, tile.Top + 40, tile.Right, tile.Bottom), _ => Rectangle.FromLTRB(tile.Left, tile.Top, tile.Right, tile.Bottom - 40)
                };
                tiles.All[dragged] = tiles.All[dragged] with { Frame = dragTo };
                engine.UserResizing(dragged);
                // A neighbor followed the edge while it was dragged.
                Assert.Contains(tiles.All.Where(item => item.Key != dragged).Select(item => item.Value.Frame), other => side switch
                {
                    0 => other.Right == dragTo.Left - 6, 1 => other.Left == dragTo.Right + 6, 2 => other.Bottom == dragTo.Top - 6, _ => other.Top == dragTo.Bottom + 6
                });
                engine.UserMoved(dragged, new Point(dragTo.X + 5, dragTo.Y + 5));
                for (var i = 0; i < 4; i++) { engine.Apply(engine.Update(windows, dragged)); time += 300; }
                var final = tiles.All[dragged].Frame;
                Assert.True(Math.Abs((side switch { 0 => final.Left - dragTo.Left, 1 => final.Right - dragTo.Right, 2 => final.Top - dragTo.Top, _ => final.Bottom - dragTo.Bottom })) <= 2,
                    $"{layout} with {count}: window {dragged}, side {side} ended at {final}, dragged to {dragTo}");
                var all = tiles.All.Values.Select(value => value.Frame).ToArray();
                Assert.All(all, frame => Assert.True(Wide.Contains(frame)));
                for (var i = 0; i < all.Length; i++) for (var j = i + 1; j < all.Length; j++) Assert.False(all[i].IntersectsWith(all[j]), $"{layout}: {all[i]} overlaps {all[j]}");
            }
    }

    [Theory]
    [InlineData("Picture in picture", false, 640, 360, true)]
    [InlineData("Picture-in-Picture", false, 1600, 900, true)]
    [InlineData("Meeting with the team", true, 480, 270, true)]
    [InlineData("Spotify", true, 300, 300, true)]
    [InlineData("Task Manager", true, 1600, 900, false)]
    [InlineData("Video player", false, 480, 270, false)]
    [InlineData("Picture in picture", false, 0, 0, false)]
    public void SmallAlwaysOnTopAndPictureInPictureWindowsArePopouts(string title, bool topmost, int width, int height, bool expected)
        => Assert.Equal(expected, Popouts.IsPopout(title, topmost, new Rectangle(100, 100, width, height), Wide));

    [Fact]
    public void LayoutSettingsAreKeptAndRepaired()
    {
        Assert.Equal(TileLayout.Dwindle, new TopBarSettings { TileLayout = (TileLayout)42 }.Normalize().TileLayout);
        var settings = new TopBarSettings().Normalize();
        Assert.True(settings.TileFixedSize); Assert.True(settings.StickyPopouts);
        Assert.Equal(TileLayout.Grid, new TopBarSettings { TileLayout = TileLayout.Grid }.Normalize().TileLayout);
    }
}
