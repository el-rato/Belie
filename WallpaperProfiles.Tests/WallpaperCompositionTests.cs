using WallpaperProfiles.Models;
using System.IO;
using WallpaperProfiles.Persistence;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class WallpaperCompositionTests
{
    [Fact]
    public void RepeatedWallpaperSwitches_RestoreTheirCompositionAndKeepGlobalWidgets()
    {
        var profile = Guid.NewGuid(); var other = Guid.NewGuid();
        var global = new DesktopWidget { Title = "Always here" };
        var defaultClock = new DesktopWidget { ProfileId = profile, Kind = DesktopWidgetKind.Clock, ClockSkin = ClockSkin.Minimal };
        var unrelated = new DesktopWidget { ProfileId = other };
        var document = new DesktopCanvasDocument { ActiveProfileId = profile, Widgets = new() { global, defaultClock, unrelated } };
        document.CustomizeWallpaper(profile, @"C:\Wallpapers\city.jpg");
        var city = Assert.Single(document.Widgets, w => w.WallpaperPath.Length > 0);
        city.ClockSkin = ClockSkin.Stacked; city.X = 600;
        foreach (var path in new[] { @"C:\Wallpapers\city.jpg", @"C:\Wallpapers\forest.jpg", @"c:/wallpapers/CITY.jpg", @"C:\Wallpapers\new.jpg", @"C:\Wallpapers\city.jpg" })
        {
            document.ActiveWallpaperPath = path;
            var widgets = document.VisibleWidgets().ToArray();
            Assert.Contains(global, widgets); Assert.DoesNotContain(unrelated, widgets); Assert.Equal(2, widgets.Length);
            var clock = widgets.Single(w => w.Kind == DesktopWidgetKind.Clock);
            Assert.Equal(path.Contains("city", StringComparison.OrdinalIgnoreCase) ? ClockSkin.Stacked : ClockSkin.Minimal, clock.ClockSkin);
        }
        Assert.Equal(160, defaultClock.X);
        document.ActiveProfileId = other;
        Assert.Equal(new[] { global.Id, unrelated.Id }, document.VisibleWidgets().Select(w => w.Id));
    }

    [Fact]
    public void EmptyCustomLayout_HidesProfileWidgetsUntilReset_WithoutHidingGlobalWidgets()
    {
        var profile = Guid.NewGuid(); var global = new DesktopWidget();
        var document = new DesktopCanvasDocument { ActiveProfileId = profile, ActiveWallpaperPath = "forest.jpg",
            Widgets = new() { global, new() { ProfileId = profile } } };
        document.CustomizeWallpaper(profile, "forest.jpg");
        document.Widgets.RemoveAll(w => w.WallpaperPath == "forest.jpg");
        Assert.Equal(global.Id, Assert.Single(document.VisibleWidgets()).Id);
        document.ResetWallpaper(profile, "forest.jpg");
        Assert.Equal(2, document.VisibleWidgets().Count());
    }

    [Fact]
    public void CustomLayoutsAndSkins_RoundTripAndLayoutEditsStayIndependent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var profile = Guid.NewGuid(); var store = new DesktopCanvasStore(path);
            var original = new DesktopWidget { ProfileId = profile, Kind = DesktopWidgetKind.Clock, Content = "Keep content", ClockSkin = ClockSkin.Analog,
                SpectrumSkin = SpectrumSkin.Ring, SpectrumDensity = 72, SpectrumThickness = 3, SpectrumSensitivity = 1.8, SpectrumSmoothing = .8 };
            var document = new DesktopCanvasDocument { Enabled = true, ActiveProfileId = profile, ActiveWallpaperPath = "forest.jpg", Widgets = new() { original } };
            document.CustomizeWallpaper(profile, "forest.jpg");
            var custom = document.VisibleWidgets().Single();
            Assert.NotEqual(original.Id, custom.Id); Assert.Equal(original.X, custom.X); Assert.Equal(original.Title, custom.Title);
            document.LayoutPresets.Add(new() { Name = "Work", Widgets = new() { custom.Copy() } });
            document.StylePresets.Add(new() { Name = "My clock", Style = custom.Copy() });
            store.Save(document); store.UpdateLayout(custom.Id, 520, 260, 640, 350);
            var loaded = store.Load(); var active = Assert.Single(loaded.VisibleWidgets());
            Assert.Equal(520, active.X); Assert.Equal(ClockSkin.Analog, active.ClockSkin);
            Assert.Equal(SpectrumSkin.Ring, active.SpectrumSkin); Assert.Equal(72, active.SpectrumDensity);
            Assert.Equal(1.8, active.SpectrumSensitivity); Assert.Equal(.8, active.SpectrumSmoothing);
            Assert.Equal("Keep content", active.Content);
            Assert.Equal(160, loaded.Widgets.Single(w => w.Id == original.Id).X);
            Assert.Equal(160, Assert.Single(Assert.Single(loaded.LayoutPresets).Widgets).X);
            Assert.Equal(ClockSkin.Analog, Assert.Single(loaded.StylePresets).Style.ClockSkin);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SameWallpaperInDifferentProfiles_KeepsIndependentLooks()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var document = new DesktopCanvasDocument { Widgets = new() { new() { ProfileId = first, Title = "First" }, new() { ProfileId = second, Title = "Second" } } };
        document.CustomizeWallpaper(first, "shared.png"); document.CustomizeWallpaper(second, "shared.png");
        document.Composition(first, "shared.png", false).Single().X = 720;
        Assert.Equal(160, document.Composition(second, "shared.png", false).Single().X);
        document.ResetWallpaper(first, "shared.png");
        Assert.False(document.HasLayout(first, "shared.png")); Assert.True(document.HasLayout(second, "shared.png"));
        Assert.Equal("Second", document.Composition(second, "shared.png", false).Single().Title);
    }
}
