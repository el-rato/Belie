using System.IO;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class DesktopCanvasTests
{
    [Fact]
    public void Canvas_RoundTripsEveryWidgetAndItsLayout()
    {
        using var fixture = new CanvasFixture();
        var document = new DesktopCanvasDocument { Enabled = true };
        foreach (var kind in Enum.GetValues<DesktopWidgetKind>()) document.Widgets.Add(new DesktopWidget
        {
            Kind = kind, Title = "My " + kind, Content = "Saved content", X = -900, Y = 130,
            Width = 380, Height = 250, Locked = true, Target = new DateTimeOffset(2026, 12, 1, 9, 30, 0, TimeSpan.FromHours(5.5))
        });
        document.Widgets[1].Enabled = false;
        fixture.Store.Save(document);
        var saved = fixture.Store.Load();
        Assert.True(saved.Enabled);
        Assert.Equal(5, saved.Widgets.Count);
        Assert.False(saved.Widgets[1].Enabled);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(document.Widgets[i].Id, saved.Widgets[i].Id);
            Assert.Equal(document.Widgets[i].Kind, saved.Widgets[i].Kind);
            Assert.Equal(-900, saved.Widgets[i].X);
            Assert.Equal(380, saved.Widgets[i].Width);
            Assert.True(saved.Widgets[i].Locked);
            Assert.Equal(document.Widgets[i].Target, saved.Widgets[i].Target);
        }
    }

    [Fact]
    public void MovingWidget_PreservesLatestContentAndDoesNotResurrectRemovedWidgets()
    {
        using var fixture = new CanvasFixture();
        var widget = new DesktopWidget { Content = "Updated in panel" };
        fixture.Store.Save(new DesktopCanvasDocument { Widgets = new() { widget } });
        Assert.True(fixture.Store.UpdateLayout(widget.Id, 440, 210, 400, 280));
        var saved = Assert.Single(fixture.Store.Load().Widgets);
        Assert.Equal("Updated in panel", saved.Content);
        Assert.Equal(440, saved.X);
        Assert.Equal(280, saved.Height);
        fixture.Store.Save(new DesktopCanvasDocument());
        Assert.False(fixture.Store.UpdateLayout(widget.Id, 100, 100, 300, 200));
        Assert.Empty(fixture.Store.Load().Widgets);
    }

    [Fact]
    public void ExistingWidgets_LoadWithAppearanceDefaults()
    {
        using var fixture = new CanvasFixture();
        File.WriteAllText(fixture.Store.FilePath, "{\"Enabled\":true,\"Widgets\":[{\"Kind\":\"Note\",\"Title\":\"Existing note\",\"Content\":\"Keep this\"}]}");
        var widget = Assert.Single(fixture.Store.Load().Widgets);
        Assert.Equal("Keep this", widget.Content);
        Assert.Equal("Segoe UI", widget.FontFamily);
        Assert.Equal("#F1F2EE", widget.TextColor);
        Assert.True(widget.ShowBackground);
        Assert.True(widget.ShowHeader);
    }

    [Fact]
    public void AppearanceAndDuplication_RoundTripWithoutSharingIdentity()
    {
        using var fixture = new CanvasFixture();
        var original = new DesktopWidget { FontFamily = "Georgia", FontSize = 32, TextColor = "#112233", BackgroundColor = "#FFEEDD",
            Opacity = .6, CornerRadius = 24, Bold = true, Alignment = WidgetTextAlignment.Right, ShowBackground = false,
            ShowBorder = false, ShowHeader = false, ImageFit = WidgetImageFit.Fill, Width = 1100, Height = 900 };
        var copy = original.Duplicate();
        fixture.Store.Save(new DesktopCanvasDocument { Widgets = new() { original, copy } });
        var saved = fixture.Store.Load().Widgets;
        Assert.NotEqual(saved[0].Id, saved[1].Id);
        Assert.Equal(original.X + 24, saved[1].X);
        Assert.Equal("Georgia", saved[1].FontFamily);
        Assert.Equal(32, saved[1].FontSize);
        Assert.Equal(.6, saved[1].Opacity);
        Assert.False(saved[1].ShowHeader);
        Assert.False(saved[1].ShowBackground);
        Assert.False(saved[1].ShowBorder);
        Assert.Equal(WidgetImageFit.Fill, saved[1].ImageFit);
        Assert.Equal(WidgetTextAlignment.Right, saved[1].Alignment);
        Assert.Equal(1100, saved[1].Width);
    }

    [Fact]
    public void ConcurrentPanelEditsAndDragging_PreserveBothChanges()
    {
        using var fixture = new CanvasFixture();
        var widget = new DesktopWidget();
        fixture.Store.Save(new DesktopCanvasDocument { Widgets = new() { widget } });
        Parallel.For(0, 60, i =>
        {
            if (i % 2 == 0) fixture.Store.Update(document => document.Widgets[0].Content += "x");
            else fixture.Store.UpdateLayout(widget.Id, 160 + i, 100, 300, 230);
        });
        var saved = Assert.Single(fixture.Store.Load().Widgets);
        Assert.Equal(new string('x', 30), saved.Content);
        Assert.InRange(saved.X, 161, 219);
    }

    [Fact]
    public void InvalidCanvas_IsReportedWithoutOverwritingIt()
    {
        using var fixture = new CanvasFixture();
        File.WriteAllText(fixture.Store.FilePath, "broken data");
        Assert.Throws<System.Text.Json.JsonException>(() => fixture.Store.Load());
        Assert.Equal("broken data", File.ReadAllText(fixture.Store.FilePath));
    }

    [Theory]
    [InlineData("https://example.com/notes", true)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("file:///C:/Windows/system32/cmd.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("example.com", false)]
    public void Links_OnlyOpenWebAddresses(string value, bool valid)
        => Assert.Equal(valid, DesktopWidget.TryGetLink(value, out _));

    [Fact]
    public void Countdown_UsesTheSavedInstantAndStopsAtZero()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var widget = new DesktopWidget { Target = now.AddDays(2).AddHours(3).AddMinutes(4) };
        Assert.Equal("2d 03h 04m", widget.CountdownText(now));
        Assert.Equal("00:00:30", widget.CountdownText(widget.Target.AddSeconds(-30)));
        Assert.Equal("It’s time", widget.CountdownText(widget.Target));
        Assert.Equal("It’s time", widget.CountdownText(widget.Target.AddDays(1)));
    }

    [Fact]
    public void SeparateCanvases_HaveIndependentHosts()
    {
        Assert.Equal(DesktopCanvasProcess.MutexName("C:/test/canvas.json"), DesktopCanvasProcess.MutexName("c:/TEST/canvas.json"));
        Assert.NotEqual(DesktopCanvasProcess.MutexName("C:/test/one.json"), DesktopCanvasProcess.MutexName("C:/test/two.json"));
    }

    [Fact]
    public void LegacyCanvas_MigratesWithoutLosingWidgets_AndKeepsBoardsIndependent()
    {
        using var fixture = new CanvasFixture();
        File.WriteAllText(fixture.Store.FilePath, "{\"Enabled\":true,\"Widgets\":[{\"Title\":\"Original\",\"Content\":\"Keep me\"}]}");
        var document = fixture.Store.Load();
        var original = Assert.Single(document.Widgets);
        Assert.Equal("Chill", document.ActiveBoard!.Name);
        var copy = DesktopCanvasFeatures.CopyBoard(document.ActiveBoard, "Study");
        document.Boards.Add(copy); document.SwitchBoard(copy.Id, new DateTime(2026, 10, 2, 12, 0, 0));
        copy.Widgets[0].Content = "Study note";
        fixture.Store.Save(document);
        Assert.True(fixture.Store.UpdateLayout(original.Id, 555, 222, 420, 240));
        var loaded = fixture.Store.Load();
        Assert.Equal("Study note", Assert.Single(loaded.Widgets).Content);
        Assert.Equal("Keep me", loaded.Boards[0].Widgets[0].Content);
        Assert.Equal(555, loaded.Boards[0].Widgets[0].X);
        Assert.NotEqual(original.Id, copy.Widgets[0].Id);
        Assert.Equal(original.Title, copy.Widgets[0].Title);
        Assert.Equal(160, copy.Widgets[0].X);
    }

    [Fact]
    public void BoardSchedule_WrapsMidnight_ResumesAfterManualSwitch_AndCatchesUpAfterSleep()
    {
        var chill = new DesktopBoard { Name = "Chill", SwitchAt = new TimeOnly(8, 0) };
        var dark = new DesktopBoard { Name = "Dark", SwitchAt = TimeOnly.MinValue };
        var manual = new DesktopBoard { Name = "Study" };
        var document = new DesktopCanvasDocument { Boards = new() { chill, dark, manual }, AutoSwitchBoards = true, ActiveBoardId = chill.Id };
        document.InitializeBoards();
        Assert.False(document.ApplyBoardSchedule(new DateTime(2026, 10, 1, 23, 59, 59)));
        Assert.True(document.ApplyBoardSchedule(new DateTime(2026, 10, 2, 0, 0, 0)));
        Assert.Equal(dark.Id, document.ActiveBoardId);
        document.SwitchBoard(manual.Id, new DateTime(2026, 10, 2, 2, 0, 0));
        Assert.Equal(new DateTime(2026, 10, 2, 8, 0, 0), document.ManualBoardUntil);
        Assert.False(document.ApplyBoardSchedule(new DateTime(2026, 10, 2, 7, 59, 0)));
        Assert.True(document.ApplyBoardSchedule(new DateTime(2026, 10, 2, 9, 35, 0)));
        Assert.Equal(chill.Id, document.ActiveBoardId);
        Assert.True(document.ApplyBoardSchedule(new DateTime(2026, 10, 4, 5, 0, 0)));
        Assert.Equal(dark.Id, document.ActiveBoardId);
        document.AutoSwitchBoards = false;
        Assert.False(document.ApplyBoardSchedule(new DateTime(2026, 10, 4, 12, 0, 0)));
    }

    [Fact]
    public void Potd_RequiresCorrectProblemOnItsUtcDay_AndKeepsTodaysStreakOpen()
    {
        var day = new DateOnly(2026, 10, 2);
        var challenges = new Dictionary<DateOnly, string> { [day] = "valid-parentheses", [day.AddDays(-1)] = "two-sum" };
        var verified = DesktopLinkService.VerifyPotd(new[] { ("two-sum", day), ("valid-parentheses", day.AddDays(-1)), ("two-sum", day.AddDays(-1)) }, challenges).ToList();
        Assert.Equal(new[] { day.AddDays(-1) }, verified);
        var widget = new DesktopWidget { PotdCompletions = verified };
        Assert.Equal(1, widget.PotdStreak(day));
        widget.PotdCompletions.Add(day);
        Assert.Equal(2, widget.PotdStreak(day));
        Assert.Equal(0, widget.PotdStreak(day.AddDays(2)));
        var copy = widget.Duplicate(); copy.PotdCompletions.Clear();
        Assert.Equal(2, widget.PotdCompletions.Count);
    }

    [Fact]
    public void LinkMetadata_HandlesAttributeOrderAndEscaping_WithoutRenderingMarkup()
    {
        var result = DesktopLinkService.ParseMetadata("<title>Fallback</title><meta content='A &amp; B' property='og:title'><meta content='Learn &lt;b&gt;code&lt;/b&gt; today' name='description'>");
        Assert.Equal("A & B", result.Title);
        Assert.Equal("Learn <b>code</b> today", result.Description);
        Assert.Equal(("", ""), DesktopLinkService.ParseMetadata("<html>Nothing here</html>"));
    }

    [Fact]
    public void SketchAndLinkDetails_RoundTripAcrossBoards_WithoutOverwritingAnExternalEdit()
    {
        using var fixture = new CanvasFixture();
        var sketch = new DesktopWidget { Kind = DesktopWidgetKind.Sketch, Drawing = "saved ink", DrawingWidth = 350, DrawingHeight = 200 };
        var link = new DesktopWidget { Kind = DesktopWidgetKind.Link, Content = "https://leetcode.com", LeetCodeUsername = "student", LinkCustomDescription = "Practice" };
        fixture.Store.Save(new DesktopCanvasDocument { Widgets = new() { sketch, link } });
        fixture.Store.Update(document => document.Widgets[0].Title = "Latest title");
        fixture.Store.UpdateWidget(sketch.Id, widget => { widget.Drawing = "new ink"; widget.DrawingWidth = 420; });
        var loaded = fixture.Store.Load();
        Assert.Equal("Latest title", loaded.Widgets[0].Title);
        Assert.Equal("new ink", loaded.Widgets[0].Drawing);
        Assert.Equal(420, loaded.Widgets[0].DrawingWidth);
        Assert.Equal("Practice", loaded.Widgets[1].LinkCustomDescription);
        Assert.True(loaded.Widgets[1].IsLeetCode);
        loaded.Widgets[1].Content = "https://leetcode.com.example.net";
        Assert.False(loaded.Widgets[1].IsLeetCode);
    }

    [Fact]
    public void TimedWallpaper_PausesRotationAndRestoresItAcrossMidnight_WithoutChangingWidgets()
    {
        using var fixture = new CanvasFixture();
        var timed = Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, "night.png"); File.WriteAllText(timed, "image");
        var widget = new DesktopWidget { Content = "Stay on the same board" };
        var board = new DesktopBoard { WallpaperMode = BoardWallpaperMode.RotatingTimed, WallpaperPath = "collection",
            RotationMinutes = 12, RotationRandom = true, OverrideWallpaperPath = timed,
            OverrideStart = new TimeOnly(22, 0), OverrideEnd = new TimeOnly(7, 0), Widgets = new() { widget } };
        var daytime = new DateTime(2026, 10, 2, 21, 59, 59);
        var night = new DateTime(2026, 10, 2, 22, 0, 0);
        var dawn = new DateTime(2026, 10, 3, 7, 0, 0);
        Assert.False(board.IsWallpaperOverrideActive(daytime));
        Assert.True(board.IsWallpaperOverrideActive(night));
        Assert.True(board.IsWallpaperOverrideActive(new DateTime(2026, 10, 3, 0, 0, 0)));
        Assert.False(board.IsWallpaperOverrideActive(dawn));
        var rotating = DesktopCanvasFeatures.SceneForBoard(board, daytime);
        Assert.Equal("collection", rotating.FolderPath); Assert.Equal(12, rotating.SlideshowIntervalMinutes); Assert.True(rotating.SlideshowRandom);
        var paused = DesktopCanvasFeatures.SceneForBoard(board, night);
        Assert.Equal(timed, paused.FolderPath); Assert.Equal(0, paused.SlideshowIntervalMinutes);
        Assert.Equal(12, DesktopCanvasFeatures.SceneForBoard(board, dawn).SlideshowIntervalMinutes);
        Assert.NotEqual(DesktopCanvasFeatures.WallpaperKey(board, daytime), DesktopCanvasFeatures.WallpaperKey(board, night));
        Assert.Equal(DesktopCanvasFeatures.WallpaperKey(board, daytime), DesktopCanvasFeatures.WallpaperKey(board, dawn));
        Assert.Same(widget, Assert.Single(board.Widgets));
        fixture.Store.Save(new DesktopCanvasDocument { Boards = new() { board } });
        var saved = fixture.Store.Load().ActiveBoard!;
        Assert.Equal(BoardWallpaperMode.RotatingTimed, saved.WallpaperMode);
        Assert.Equal(timed, saved.OverrideWallpaperPath); Assert.Equal(new TimeOnly(7, 0), saved.OverrideEnd);
        var copy = DesktopCanvasFeatures.CopyBoard(saved, "Copy"); Assert.Equal(12, copy.RotationMinutes); Assert.Equal(timed, copy.OverrideWallpaperPath);
    }

    [Fact]
    public void NormalAndTimedModes_DoNotInheritRotation_AndLegacyProfilesKeepTheirRotation()
    {
        using var fixture = new CanvasFixture();
        var image = Path.Combine(Path.GetDirectoryName(fixture.Store.FilePath)!, "alternate.png"); File.WriteAllText(image, "image");
        var board = new DesktopBoard { WallpaperPath = "original.png", Scene = new WallpaperProfiles.Models.WallpaperProfile { SlideshowIntervalMinutes = 15 },
            OverrideWallpaperPath = image, OverrideStart = new TimeOnly(10, 0), OverrideEnd = new TimeOnly(12, 0) };
        var now = new DateTime(2026, 10, 2, 11, 0, 0);
        Assert.Equal(BoardWallpaperMode.Rotating, board.EffectiveWallpaperMode);
        Assert.Equal(15, DesktopCanvasFeatures.SceneForBoard(board, now).SlideshowIntervalMinutes);
        board.WallpaperMode = BoardWallpaperMode.Normal;
        Assert.Equal("original.png", DesktopCanvasFeatures.SceneForBoard(board, now).FolderPath);
        Assert.Equal(0, DesktopCanvasFeatures.SceneForBoard(board, now).SlideshowIntervalMinutes);
        board.WallpaperMode = BoardWallpaperMode.Timed;
        Assert.Equal(image, DesktopCanvasFeatures.SceneForBoard(board, now).FolderPath);
        Assert.Equal("original.png", DesktopCanvasFeatures.SceneForBoard(board, now.AddHours(1)).FolderPath);
        File.Delete(image);
        Assert.Equal("original.png", DesktopCanvasFeatures.SceneForBoard(board, now).FolderPath);
        board.OverrideEnd = board.OverrideStart;
        Assert.False(board.IsWallpaperOverrideActive(now));
    }

    private sealed class CanvasFixture : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "belie-canvas-test-" + Guid.NewGuid().ToString("N"));
        public DesktopCanvasStore Store { get; }
        public CanvasFixture() { Directory.CreateDirectory(_dir); Store = new DesktopCanvasStore(Path.Combine(_dir, "canvas.json")); }
        public void Dispose() => Directory.Delete(_dir, recursive: true);
    }
}
