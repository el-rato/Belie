using System.IO;
using System.Text.Json;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class WidgetAndBarOptionTests
{
    [Fact]
    public void EarlierBarSettingsBecomeModules()
    {
        var settings = JsonSerializer.Deserialize<TopBarSettings>("""{ "Enabled": true, "ShowTitle": false, "ShowMedia": true, "ShowCpu": false, "ShowBattery": false }""", JsonOptions.Shared)!.Normalize();
        Assert.Equal(new[] { BarModuleKind.Workspaces }, settings.Left!.Select(module => module.Kind));
        Assert.Equal(new[] { BarModuleKind.Clock }, settings.Center!.Select(module => module.Kind));
        Assert.Equal(new[] { BarModuleKind.Media, BarModuleKind.Memory, BarModuleKind.Volume }, settings.Right!.Select(module => module.Kind));
        Assert.DoesNotContain("ShowTitle", JsonSerializer.Serialize(settings, JsonOptions.Shared));
    }

    [Fact]
    public void ModulesAppearOnceUnlessTheyCarryTheirOwnText()
    {
        var settings = new TopBarSettings
        {
            Left = new() { new(BarModuleKind.Clock), new(BarModuleKind.Text) { Text = "a" }, new((BarModuleKind)99) },
            Center = new() { new(BarModuleKind.Clock), new(BarModuleKind.Text) { Text = "b" } }, Right = new()
        }.Normalize();
        Assert.Equal(2, settings.Left!.Count);
        Assert.Equal("b", Assert.Single(settings.Center!).Text);
        Assert.Empty(settings.Right!); // Emptied on purpose, so nothing is put back.
    }

    [Fact]
    public void ClockFormatsThatMakeNoSenseFallBack()
    {
        var now = new DateTime(2026, 11, 25, 9, 5, 7);
        Assert.Equal("09:05:07", TopBarWindow.Format(now, "HH':'mm':'ss", "HH"));
        Assert.Equal("9", TopBarWindow.Format(now, "H", "mm"));
        Assert.Equal("05", TopBarWindow.Format(now, "%", "mm"));
        Assert.Equal("05", TopBarWindow.Format(now, "", "mm"));
    }

    [Fact]
    public void WeatherIsReadFromAForecast()
    {
        using var json = JsonDocument.Parse("""
            { "current": { "temperature_2m": 21.4, "apparent_temperature": 20.1, "relative_humidity_2m": 56, "weather_code": 61, "is_day": 0, "wind_speed_10m": 12.2 },
              "daily": { "time": ["2026-11-25", "2026-11-26"], "weather_code": [61, 0], "temperature_2m_max": [23.0, 24.6], "temperature_2m_min": [14.2, 13.9] } }
            """);
        var report = WeatherSource.Parse("Lisbon", json.RootElement, false);
        Assert.Equal("21°", report.Degrees(report.Temperature)); Assert.False(report.IsDay); Assert.Equal("12 km/h", report.WindText);
        Assert.Equal(new DateOnly(2026, 11, 26), report.Days[1].Date); Assert.Equal(24.6, report.Days[1].High);
        Assert.NotEqual("", WeatherSource.Describe(61)); Assert.NotEqual("", WeatherSource.Symbol(61, true));
    }

    [Fact]
    public async Task NoPlaceMeansNoWeatherRequest() => Assert.Null(await WeatherSource.GetAsync("  ", false));

    [Fact]
    public void WallpaperColorsStayReadable()
    {
        var pixels = Enumerable.Range(0, 400).Select(index => index % 5 == 0 ? 0x2244DD : 0x101820).ToArray();
        var colors = WallpaperColors.From(pixels);
        static double Light(Color color) => (.3 * color.R + .59 * color.G + .11 * color.B) / 255;
        Assert.True(Light(colors.Background) < .25); Assert.True(Light(colors.Text) > .75);
        Assert.True(colors.Accent.B > colors.Accent.R);
    }

    [Fact]
    public void NewWidgetOptionsAreStoredAndKeptInRange()
    {
        var path = Path.Combine(Path.GetTempPath(), "belie-widgets-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new DesktopCanvasStore(path);
            store.Save(new DesktopCanvasDocument
            {
                Widgets = new()
                {
                    new() { Kind = DesktopWidgetKind.System, SystemSkin = SystemSkin.Graph, SystemMetrics = SystemMetrics.Cpu | SystemMetrics.Network | (SystemMetrics)1024 },
                    new() { Kind = DesktopWidgetKind.Timer, TimerMinutes = 9000, TimerBreakMinutes = -2 },
                    new() { Kind = DesktopWidgetKind.NowPlaying, MusicLayout = NowPlayingLayout.Vinyl, MusicArtBackdrop = true, MusicColorsFromArt = true },
                    new() { Kind = DesktopWidgetKind.Weather, WeatherSkin = (WeatherSkin)40, WeatherFahrenheit = true, Content = "Lisbon" }
                }
            });
            var saved = store.Load().Widgets;
            Assert.Equal(SystemMetrics.Cpu | SystemMetrics.Network, saved[0].SystemMetrics); Assert.Equal(SystemSkin.Graph, saved[0].SystemSkin);
            Assert.Equal((240, 1), (saved[1].TimerMinutes, saved[1].TimerBreakMinutes));
            Assert.True(saved[2].MusicArtBackdrop && saved[2].MusicColorsFromArt); Assert.Equal(NowPlayingLayout.Vinyl, saved[2].MusicLayout);
            Assert.Equal(WeatherSkin.Forecast, saved[3].WeatherSkin); Assert.True(saved[3].WeatherFahrenheit);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ChecklistItemsAreTickedOneAtATime()
    {
        var content = ChecklistWidgetView.Toggle("Milk\r\n\r\n[x] Bread", 0);
        Assert.Equal("[x] Milk\n[x] Bread", content);
        Assert.Equal("[x] Milk\nBread", ChecklistWidgetView.Toggle(content, 1));
        Assert.Equal(content, ChecklistWidgetView.Toggle(content, 7));
    }

    [Fact]
    public void FocusTimerPausesAndTurnsIntoABreak() => OnUiThread(() =>
    {
        var now = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
        var widget = new DesktopWidget { Kind = DesktopWidgetKind.Timer, TimerMinutes = 10, TimerBreakMinutes = 3 };
        var timer = new TimerWidgetView(widget, () => now) { PreviewMode = true };
        Assert.False(timer.IsRunning); Assert.Equal(TimeSpan.FromMinutes(10), timer.Left);
        timer.Toggle(); now = now.AddMinutes(4);
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Left);
        timer.Toggle(); now = now.AddMinutes(30);
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Left);
        timer.UpdateAppearance(widget); // Restyling must not restart it.
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Left);
        timer.Toggle(); now = now.AddMinutes(6); timer.UpdateAppearance(widget);
        Assert.True(timer.IsBreak); Assert.Equal(TimeSpan.FromMinutes(3), timer.Left);
        timer.Reset();
        Assert.False(timer.IsBreak); Assert.False(timer.IsRunning);
        widget.TimerMinutes = 20; timer.UpdateAppearance(widget);
        Assert.Equal(TimeSpan.FromMinutes(20), timer.Left);
    });

    [Fact]
    public void GalleryOffersEveryPlayerAndNewWidget() => OnUiThread(() =>
    {
        var entries = WidgetStyles.Catalogue;
        Assert.All(Enum.GetValues<NowPlayingLayout>(), layout => Assert.Contains(entries, entry => entry.Widget.Kind == DesktopWidgetKind.NowPlaying && entry.Widget.MusicLayout == layout));
        Assert.All(Enum.GetValues<SystemSkin>(), skin => Assert.Contains(entries, entry => entry.Widget.Kind == DesktopWidgetKind.System && entry.Widget.SystemSkin == skin));
        Assert.All(Enum.GetValues<WeatherSkin>(), skin => Assert.Contains(entries, entry => entry.Widget.Kind == DesktopWidgetKind.Weather && entry.Widget.WeatherSkin == skin));
        Assert.Equal(entries.Count, entries.Select(entry => entry.Name).Distinct().Count());
        foreach (var entry in entries)
        {
            var copy = entry.Widget.Copy();
            DesktopCanvasStore.NormalizeWidget(copy);
            Assert.Equal((entry.Widget.Width, entry.Widget.Height, entry.Widget.FontSize), (copy.Width, copy.Height, copy.FontSize));
            var target = new DesktopWidget { Kind = copy.Kind };
            WidgetStyles.Apply(target, copy);
            Assert.Equal((copy.MusicLayout, copy.MusicArtBackdrop, copy.SystemSkin, copy.SystemMetrics, copy.WeatherSkin), (target.MusicLayout, target.MusicArtBackdrop, target.SystemSkin, target.SystemMetrics, target.WeatherSkin));
            var preview = WidgetStyles.Preview(copy);
            preview.Measure(new System.Windows.Size(600, 600)); preview.Arrange(new System.Windows.Rect(preview.DesiredSize));
        }
    });

    [Fact]
    public void EveryTransitionReachesEveryCell()
    {
        foreach (var style in Enum.GetValues<WallpaperTransitionStyle>().Except(new[] { WallpaperTransitionStyle.None, WallpaperTransitionStyle.Fade, WallpaperTransitionStyle.Random }))
        {
            var field = TransitionField.For(style, 80, 45, () => null, new Random(3))!;
            Assert.Equal(80 * 45, field.Order.Distinct().Count());
            Assert.InRange(field.Arrival.Min(), 0, .05f); Assert.InRange(field.Arrival.Max(), .9f, 1f);
        }
        Assert.Null(TransitionField.For(WallpaperTransitionStyle.Fade, 80, 45, () => null, new Random(3)));
        for (var seed = 0; seed < 40; seed++)
            Assert.DoesNotContain(TransitionField.Resolve(WallpaperTransitionStyle.Random, new Random(seed)), new[] { WallpaperTransitionStyle.Random, WallpaperTransitionStyle.None });
        var right = TransitionField.Wipe(80, 45, 0, false); var down = TransitionField.Wipe(80, 45, 90, false);
        Assert.True(right.Arrival[0] < right.Arrival[79] && down.Arrival[0] < down.Arrival[44 * 80]);
    }

    [Theory]
    [InlineData(WallpaperTransitionStyle.Fade, true)]
    [InlineData(WallpaperTransitionStyle.Wave, true)]
    [InlineData(WallpaperTransitionStyle.Dissolve, false)]
    public void PaintingEndsOnExactlyTheNewPicture(WallpaperTransitionStyle style, bool glow)
    {
        const int width = 130, height = 70;
        var shown = new int[width * height];
        Array.Fill(shown, 0x203040);
        using var next = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) next.SetPixel(x, y, System.Drawing.Color.FromArgb(x, y * 3, 200));
        var held = System.Runtime.InteropServices.GCHandle.Alloc(shown, System.Runtime.InteropServices.GCHandleType.Pinned);
        using (var painter = new TransitionPainter(held.AddrOfPinnedObject(), width, height))
        {
            var plan = WallpaperTransition.Plan(next, new TransitionOptions(style, Cell: 8, Glow: glow), new Random(1));
            painter.Begin(next, plan.Field, plan.Glow, plan.Line, plan.Options);
            painter.Draw(0); painter.Draw(.5f);
            Assert.Contains(shown, pixel => (pixel & 0xFFFFFF) != 0x203040);
            painter.Draw(1);
        }
        held.Free();
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) Assert.Equal(x << 16 | y * 3 << 8 | 200, shown[y * width + x] & 0xFFFFFF);
    }

    private static void OnUiThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
