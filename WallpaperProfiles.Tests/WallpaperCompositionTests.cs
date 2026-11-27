using WallpaperProfiles.Models;
using System.IO;
using System.Text.Json;
using WallpaperProfiles.Persistence;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class WallpaperCompositionTests
{
    [Fact]
    public void WidgetShowsOnEveryWallpaper_AtItsOwnSpotWhereMoved()
    {
        var profile = Guid.NewGuid(); var other = Guid.NewGuid();
        var global = new DesktopWidget { Title = "Always here" };
        var clock = new DesktopWidget { ProfileId = profile, Kind = DesktopWidgetKind.Clock, X = 100, Y = 100 };
        var unrelated = new DesktopWidget { ProfileId = other };
        var document = new DesktopCanvasDocument { ActiveProfileId = profile, Widgets = new() { global, clock, unrelated } };
        clock.Place(@"C:\Wallpapers\city.jpg", 900, 40, 400, 260);
        foreach (var path in new[] { @"C:\Wallpapers\city.jpg", @"C:\Wallpapers\forest.jpg", @"c:/wallpapers/CITY.jpg", @"C:\Wallpapers\new.jpg", "" })
        {
            document.ActiveWallpaperPath = path;
            var widgets = document.VisibleWidgets().ToArray();
            // Never removed from any wallpaper of the profile.
            Assert.Equal(new[] { global.Id, clock.Id }, widgets.Select(w => w.Id));
            var shown = widgets.Single(w => w.Id == clock.Id);
            var onCity = path.Contains("city", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(onCity ? 900 : 100, shown.X); Assert.Equal(onCity ? 400 : 300, shown.Width);
        }
        // The stored widget keeps its default spot.
        Assert.Equal(100, clock.X);
        document.ActiveProfileId = other;
        Assert.Equal(new[] { global.Id, unrelated.Id }, document.VisibleWidgets().Select(w => w.Id));
    }

    [Fact]
    public void ResetPlacement_ReturnsToDefaultSpot_OnlyForThatWallpaper()
    {
        var widget = new DesktopWidget { X = 10, Y = 20 };
        widget.Place("a.jpg", 500, 500, 300, 230); widget.Place("b.jpg", 700, 100, 300, 230);
        Assert.True(widget.ResetPlacement("A.JPG"));
        Assert.Equal(10, widget.PlacedOn("a.jpg").X);
        Assert.Equal(700, widget.PlacedOn("b.jpg").X);
        Assert.False(widget.ResetPlacement("a.jpg"));
        widget.Place("", 42, 43, 300, 230);
        Assert.Equal(42, widget.X); Assert.Single(widget.Placements);
    }

    [Fact]
    public void HiddenOnWallpaper_HidesOnlyThere_AndOnlyInThatProfile()
    {
        var profile = Guid.NewGuid(); var other = Guid.NewGuid();
        var global = new DesktopWidget { Title = "Everywhere" };
        var clock = new DesktopWidget { ProfileId = profile, Kind = DesktopWidgetKind.Clock };
        var document = new DesktopCanvasDocument { ActiveProfileId = profile, Widgets = new() { global, clock } };
        Assert.True(clock.SetHiddenOn(profile, @"C:\Wallpapers\city.jpg", true));
        Assert.True(global.SetHiddenOn(profile, @"C:\Wallpapers\city.jpg", true));
        Assert.False(clock.SetHiddenOn(profile, "c:/wallpapers/CITY.jpg", true));
        Assert.False(clock.SetHiddenOn(profile, "", true));

        document.ActiveWallpaperPath = "c:/wallpapers/CITY.jpg";
        Assert.Empty(document.VisibleWidgets());
        document.ActiveWallpaperPath = @"C:\Wallpapers\forest.jpg";
        Assert.Equal(new[] { global.Id, clock.Id }, document.VisibleWidgets().Select(w => w.Id));
        // A general widget hidden on city.jpg in one profile still shows there in another.
        document.ActiveProfileId = other; document.ActiveWallpaperPath = @"C:\Wallpapers\city.jpg";
        Assert.Equal(new[] { global.Id }, document.VisibleWidgets().Select(w => w.Id));

        Assert.True(clock.SetHiddenOn(profile, @"C:\WALLPAPERS\city.jpg", false));
        document.ActiveProfileId = profile;
        Assert.Equal(new[] { clock.Id }, document.VisibleWidgets().Select(w => w.Id));
        // Copies keep their own hidden list.
        var copy = global.Copy(); copy.SetHiddenOn(profile, @"C:\Wallpapers\city.jpg", false);
        Assert.True(global.IsHiddenOn(profile, @"C:\Wallpapers\city.jpg"));
    }

    [Fact]
    public void HiddenOnWallpaper_RoundTripsThroughStore()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var profile = Guid.NewGuid(); var store = new DesktopCanvasStore(path);
            var note = new DesktopWidget { ProfileId = profile };
            note.SetHiddenOn(profile, "forest.jpg", true);
            store.Save(new DesktopCanvasDocument { Enabled = true, ActiveProfileId = profile, ActiveWallpaperPath = "forest.jpg", Widgets = new() { note } });
            Assert.Empty(store.Load().VisibleWidgets());
            store.Update(saved => saved.ActiveWallpaperPath = "city.jpg");
            Assert.Single(store.Load().VisibleWidgets());
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"HiddenOn\":", "\"Ignored\":"));
            Assert.Empty(store.Load().Widgets.Single().HiddenOn);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DesktopDrag_SavesSpotForTheWallpaperOnScreen_AndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var profile = Guid.NewGuid(); var store = new DesktopCanvasStore(path);
            var clock = new DesktopWidget { ProfileId = profile, Kind = DesktopWidgetKind.Clock, ClockSkin = ClockSkin.Analog, SpectrumDensity = 72 };
            var document = new DesktopCanvasDocument { Enabled = true, ActiveProfileId = profile, ActiveWallpaperPath = "forest.jpg", Widgets = new() { clock } };
            document.StylePresets.Add(new() { Name = "My clock", Style = clock.Copy() });
            store.Save(document);
            Assert.True(store.UpdateLayout(clock.Id, 520, 260, 640, 350));
            var loaded = store.Load();
            var onForest = Assert.Single(loaded.VisibleWidgets());
            Assert.Equal(520, onForest.X); Assert.Equal(640, onForest.Width); Assert.Equal(ClockSkin.Analog, onForest.ClockSkin);
            Assert.Equal(160, loaded.Widgets.Single().X);
            store.Update(saved => saved.ActiveWallpaperPath = "city.jpg");
            Assert.Equal(160, Assert.Single(store.Load().VisibleWidgets()).X);
            // A copy (preset, duplicate) keeps its own independent spots.
            var copy = store.Load().Widgets.Single().Duplicate();
            Assert.Equal(544, copy.PlacedOn("forest.jpg").X);
            Assert.Equal(520, store.Load().Widgets.Single().PlacedOn("forest.jpg").X);
            Assert.Equal(ClockSkin.Analog, Assert.Single(store.Load().StylePresets).Style.ClockSkin);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OldWallpaperLayouts_MigrateIntoSpotsOnTheOriginalWidget()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var profile = Guid.NewGuid();
            var original = new DesktopWidget { ProfileId = profile, Title = "Clock", Kind = DesktopWidgetKind.Clock, X = 100, Y = 100 };
            var untouched = new DesktopWidget { ProfileId = profile, Title = "Note", X = 50, Y = 50 };
            // Belie 3.4.2 data: city.jpg had its own copies, one moved, plus a widget made only there.
            var movedCopy = new DesktopWidget { ProfileId = profile, Title = "Clock", Kind = DesktopWidgetKind.Clock, X = 800, Y = 40, WallpaperPath = "city.jpg" };
            var sameCopy = new DesktopWidget { ProfileId = profile, Title = "Note", X = 50, Y = 50, WallpaperPath = "city.jpg" };
            var onlyThere = new DesktopWidget { ProfileId = profile, Title = "Only city", X = 10, Y = 10, WallpaperPath = "city.jpg" };
            var legacy = new
            {
                Enabled = true, ActiveProfileId = profile, ActiveWallpaperPath = "forest.jpg",
                Widgets = new[] { original, untouched, movedCopy, sameCopy, onlyThere },
                WallpaperLayouts = new[] { new { ProfileId = profile, WallpaperPath = "city.jpg" } },
            };
            File.WriteAllText(path, JsonSerializer.Serialize(legacy));
            var loaded = new DesktopCanvasStore(path).Load();
            Assert.Empty(loaded.WallpaperLayouts);
            Assert.Equal(new[] { "Clock", "Note", "Only city" }, loaded.Widgets.Select(w => w.Title).Order());
            Assert.All(loaded.Widgets, w => Assert.Equal("", w.WallpaperPath));
            var clock = loaded.Widgets.Single(w => w.Title == "Clock");
            Assert.Equal(original.Id, clock.Id);
            Assert.Equal(100, clock.X); Assert.Equal(800, clock.PlacedOn("city.jpg").X);
            Assert.Empty(loaded.Widgets.Single(w => w.Title == "Note").Placements);
            loaded.ActiveWallpaperPath = "forest.jpg";
            Assert.Equal(3, loaded.VisibleWidgets().Count());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SameWallpaperInDifferentProfiles_KeepsIndependentSpots()
    {
        var first = new DesktopWidget { ProfileId = Guid.NewGuid() };
        var second = new DesktopWidget { ProfileId = Guid.NewGuid() };
        first.Place("shared.png", 720, 0, 300, 230);
        Assert.Equal(720, first.PlacedOn("shared.png").X);
        Assert.Equal(160, second.PlacedOn("shared.png").X);
    }
}
