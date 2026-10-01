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
    public void SketchAndLinkDetails_RoundTripWithoutOverwritingAnExternalEdit()
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
    public void RemovingBoards_PreservesAllWidgetsAndBacksUpTheOriginalCanvas()
    {
        using var fixture = new CanvasFixture();
        var active = new DesktopWidget { Kind = DesktopWidgetKind.Sketch, Drawing = "saved ink" };
        var other = new DesktopWidget { Kind = DesktopWidgetKind.Link, Content = "https://leetcode.com", LeetCodeUsername = "student", LinkCustomDescription = "Practice" };
        var original = System.Text.Json.JsonSerializer.Serialize(new { Enabled = true, Widgets = new[] { active }, Boards = new[] {
            new { Widgets = new[] { active } }, new { Widgets = new[] { other } } } }, JsonOptions.Shared);
        File.WriteAllText(fixture.Store.FilePath, original);
        var loaded = fixture.Store.Load();
        Assert.Equal(2, loaded.Widgets.Count); Assert.True(loaded.Widgets[0].Enabled); Assert.False(loaded.Widgets[1].Enabled);
        Assert.Equal("saved ink", loaded.Widgets[0].Drawing); Assert.Equal("Practice", loaded.Widgets[1].LinkCustomDescription);
        fixture.Store.Update(document => document.Widgets[0].Title = "Updated sketch");
        Assert.Equal(original, File.ReadAllText(fixture.Store.FilePath + ".boards-backup.json"));
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fixture.Store.FilePath));
        Assert.False(json.RootElement.TryGetProperty("Boards", out _));
        Assert.Equal(2, fixture.Store.Load().Widgets.Count);
    }

    [Fact]
    public void MigratedWidgets_AreNotResurrectedAfterDeletion()
    {
        using var fixture = new CanvasFixture();
        var widget = new DesktopWidget { Kind = DesktopWidgetKind.Sketch };
        File.WriteAllText(fixture.Store.FilePath, System.Text.Json.JsonSerializer.Serialize(new { Widgets = new[] { widget }, Boards = new[] {
            new { Widgets = new[] { widget } } } }, JsonOptions.Shared));
        fixture.Store.Update(document => document.Widgets.Clear());
        Assert.Empty(fixture.Store.Load().Widgets);
        Assert.False(fixture.Store.UpdateLayout(widget.Id, 10, 10, 200, 200));
        Assert.Empty(fixture.Store.Load().Widgets);
    }

    private sealed class CanvasFixture : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "belie-canvas-test-" + Guid.NewGuid().ToString("N"));
        public DesktopCanvasStore Store { get; }
        public CanvasFixture() { Directory.CreateDirectory(_dir); Store = new DesktopCanvasStore(Path.Combine(_dir, "canvas.json")); }
        public void Dispose() => Directory.Delete(_dir, recursive: true);
    }
}
