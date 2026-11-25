using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class BarRiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("belie-rice").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static BarPalette Palette() => new(Colors.Black, Colors.White, Color.FromRgb(200, 100, 50), Colors.Gray);

    [Theory]
    [InlineData("#abc", 255, 0xAA, 0xBB, 0xCC)]
    [InlineData("#11223380", 0x80, 0x11, 0x22, 0x33)]
    [InlineData("rgba(10, 20, 30, .5)", 128, 10, 20, 30)]
    [InlineData("rgb(10 20 30 / 50%)", 128, 10, 20, 30)]
    [InlineData("@accent", 255, 200, 100, 50)]
    [InlineData("alpha(@accent, 0.5)", 128, 200, 100, 50)]
    [InlineData("shade(@accent, .5)", 255, 100, 50, 25)]
    [InlineData("mix(black, white, 0.5)", 255, 128, 128, 128)]
    [InlineData("transparent", 0, 255, 255, 255)]
    public void Colors_AreRead(string text, byte a, byte r, byte g, byte b)
    {
        Assert.True(Palette().TryColor(text, out var color));
        if (a == 0) Assert.Equal(0, color.A); else Assert.Equal(Color.FromArgb(a, r, g, b), color);
    }

    [Theory]
    [InlineData("")] [InlineData("#12")] [InlineData("@nothing")] [InlineData("rgb(1,2)")] [InlineData("alpha(red)")] [InlineData("url(file:///c:/a.png)")] [InlineData("not a color")]
    public void WhatIsNotAColor_IsRefused(string text) => Assert.False(Palette().TryColor(text, out _));

    [Fact]
    public void Gradients_PointAsOnTheWeb()
    {
        var right = Assert.IsType<LinearGradientBrush>(Palette().TryBrush("linear-gradient(90deg, #f00, rgba(0, 0, 255, .5) 80%)"));
        Assert.Equal(0, right.StartPoint.X, 6); Assert.Equal(1, right.EndPoint.X, 6); Assert.Equal(.5, right.EndPoint.Y, 6);
        Assert.Equal(.8, right.GradientStops[1].Offset, 6);
        Assert.Null(Palette().TryBrush("linear-gradient(90deg, red)"));
    }

    [Fact]
    public void Stylesheet_CascadesByWeightAndOrder()
    {
        var sheet = BarStylesheet.Parse("""
            /* comment { } */
            @define-color hot #ff0000;
            * { color: white; }
            #clock { color: blue; padding: 2px 4px }
            .left #clock { color: @hot; }
            #clock { color: green; }
            #workspaces button.focused, #pulseaudio:hover { color: yellow; }
            """);
        Assert.Empty(sheet.Problems);
        Assert.Equal("hot", Assert.Single(sheet.Colors).Name);
        BarLook Look(BarElement element) { var palette = Palette(); palette.Define(sheet.Colors); return BarLook.From(sheet.For(element), palette, 13); }
        Color ColorOf(BarElement element) => ((SolidColorBrush)Look(element).Color!).Color;
        BarElement Clock(string part) => new("module", "clock", "module clock", new BarElement("part", "", part, new BarElement("bar")));
        Assert.Equal(Colors.Red, ColorOf(Clock("left")));
        Assert.Equal(Colors.Green, ColorOf(Clock("right")));
        Assert.Equal(new Thickness(4, 2, 4, 2), Look(Clock("left")).Padding);
        var spaces = new BarElement("module", "workspaces", "module workspaces");
        Assert.Equal(Colors.Yellow, ColorOf(new BarElement("button", "", "active", spaces)));
        Assert.Equal(Colors.White, ColorOf(new BarElement("button", "", "occupied", spaces)));
        var volume = new BarElement("module", "volume", "module volume");
        Assert.Equal(Colors.White, ColorOf(volume));
        Assert.Equal(Colors.Yellow, ColorOf(volume with { Classes = "module volume hover" }));
    }

    [Fact]
    public void Stylesheet_ReportsMistakesAndKeepsTheRest()
    {
        var sheet = BarStylesheet.Parse("""
            #clock {
              color: blurple;
              font-size: 14px;
              wobble: 3;
            }
            @import "other.css";
            #cpu > { color: red; }
            #memory { color: red
            """);
        Assert.Contains(sheet.Problems, problem => problem.StartsWith("Line 2:"));
        Assert.Contains(sheet.Problems, problem => problem.StartsWith("Line 4:"));
        Assert.Contains(sheet.Problems, problem => problem.StartsWith("Line 6:"));
        Assert.Contains(sheet.Problems, problem => problem.StartsWith("Line 8:"));
        Assert.Equal(14, BarLook.From(sheet.For(new BarElement("module", "clock")), Palette(), 13).Size);
        foreach (var broken in new[] { "{", "}", "#", "a{b:c", "/*", "@", "a{}}", ":::{x:y}", "bar{border:1px solid linear-gradient(" }) BarStylesheet.Parse(broken);
    }

    [Fact]
    public void Looks_LeaveBehaviorAlone()
    {
        var profile = Guid.NewGuid();
        var mine = new TopBarSettings
        {
            Enabled = true, WorkspaceCount = 4, HideEmptyWorkspaces = true, WorkspaceProfiles = new() { [2] = profile }, WorkspaceNames = new() { "Web" }, TilingEnabled = false, TileGap = 21,
            PinnedApps = new() { "spotify" }, FloatingApps = new() { "calc" }, Motion = WorkspaceMotion.Fade, WeatherPlace = "Lisbon", Shortcuts = WorkspaceShortcutModifier.CtrlAlt
        }.Normalize();
        foreach (var rice in BarRice.BuiltIn)
        {
            Assert.Empty(BarStylesheet.Parse(rice.Css).Problems);
            Assert.Empty(rice.Commands);
            var next = rice.ApplyTo(mine);
            Assert.Equal(rice.Css, next.Css);
            Assert.Equal(rice.Look.Normalize().Modules.Select(module => module.Kind), next.Modules.Select(module => module.Kind));
            Assert.True(next.Enabled); Assert.True(next.HideEmptyWorkspaces); Assert.False(next.TilingEnabled);
            Assert.Equal((4, 21, profile, "Lisbon"), (next.WorkspaceCount, next.TileGap, next.WorkspaceProfiles[2], next.WeatherPlace));
            Assert.Equal((WorkspaceMotion.Fade, WorkspaceShortcutModifier.CtrlAlt), (next.Motion, next.Shortcuts));
            Assert.Equal(new[] { "spotify" }, next.PinnedApps); Assert.Equal(new[] { "calc" }, next.FloatingApps);
        }
        Assert.Equal(BarRice.BuiltIn.Count, BarRice.BuiltIn.Select(rice => rice.Name).Distinct().Count());
    }

    [Fact]
    public void SharedLooks_HoldNothingPersonal_AndRoundTrip()
    {
        var mine = new TopBarSettings { Enabled = true, WorkspaceNames = new() { "Secret" }, WorkspaceProfiles = new() { [1] = Guid.NewGuid() }, WeatherPlace = "Home town", PinnedApps = new() { "diary" }, Height = 44 }.Normalize();
        mine.Css = "#clock { color: red; }";
        mine.Right!.Add(new BarModule(BarModuleKind.Script) { Command = "echo hi", Style = { ["color"] = "#fff" }, OnClick = "belie:wallpapers" });
        mine.Right!.Add(new BarModule(BarModuleKind.Text) { Text = "x", OnClick = "evil.exe" });
        mine.Left!.Insert(0, new BarModule(BarModuleKind.Wallpaper) { OnRightClick = "belie:next-wallpaper" });
        var store = new BarRiceStore(Path.Combine(_dir, "looks"));
        var path = store.Save(BarRice.From("My: look?", mine));
        var text = File.ReadAllText(path);
        foreach (var word in new[] { "Secret", "Home town", "diary" }) Assert.DoesNotContain(word, text);
        File.WriteAllText(Path.Combine(store.Folder, "broken" + BarRiceStore.Extension), "{");
        var (_, rice) = Assert.Single(store.Load());
        Assert.Equal("My: look?", rice.Name); Assert.Equal(44, rice.Look.Height); Assert.Equal(mine.Css, rice.Css);
        Assert.Equal(new[] { "echo hi", "evil.exe" }, rice.Commands.OrderBy(command => command));
        rice.RemoveCommands();
        Assert.Empty(rice.Commands);
        Assert.DoesNotContain(rice.Look.Modules, module => module.Kind == BarModuleKind.Script);
        Assert.Equal("belie:next-wallpaper", rice.Look.Left![0].OnRightClick);
        Assert.Contains(rice.Look.Modules, module => module.Kind == BarModuleKind.Text);
    }

    [Fact]
    public void Stylesheet_IsKeptBesideTheSettings()
    {
        var store = new TopBarStore(Path.Combine(_dir, "topbar.json"));
        store.SaveCss("");
        Assert.False(File.Exists(store.CssPath));
        store.SaveCss("#clock { color: red; }");
        store.Save(new TopBarSettings { Height = 40 });
        Assert.DoesNotContain("#clock", File.ReadAllText(store.FilePath));
        Assert.Equal("#clock { color: red; }", store.Load().Css);
        File.WriteAllText(store.CssPath, "bar { }");
        Assert.Equal("bar { }", store.Load().Css);
    }

    [Fact]
    public async Task Scripts_RunWhenDue_AndNeverTwiceAtOnce()
    {
        var now = new DateTime(2026, 1, 1);
        var pending = new Queue<TaskCompletionSource<string>>();
        var read = new List<(string Text, string Tip)>();
        using var scripts = new BarScripts(() => now, (_, _) => { var next = new TaskCompletionSource<string>(); pending.Enqueue(next); return next.Task; });
        scripts.Read += (_, text, tip) => read.Add((text, tip));
        var modules = new[] { new BarModule(BarModuleKind.Script) { Command = "a", Interval = 5 }, new BarModule(BarModuleKind.Text) { Command = "b" }, new BarModule(BarModuleKind.Script) };
        scripts.Tick(modules); scripts.Tick(modules);
        now = now.AddSeconds(6);
        scripts.Tick(modules);
        Assert.Single(pending);
        pending.Dequeue().SetResult("21%\r\nline two\r\nline three\r\n");
        await Task.Yield();
        Assert.Equal(("21%", "line two\nline three"), Assert.Single(read));
        scripts.Tick(modules);
        pending.Dequeue().SetException(new InvalidOperationException("no"));
        Assert.Equal(("failed", "no"), read[1]);
        scripts.Tick(modules);
        Assert.Empty(pending);
        now = now.AddSeconds(5);
        scripts.Tick(modules);
        Assert.Single(pending);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Scripts_AreReallyRun()
    {
        var done = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scripts = new BarScripts();
        scripts.Read += (_, text, tip) => done.TrySetResult((text, tip));
        scripts.Tick(new[] { new BarModule(BarModuleKind.Script) { Command = "echo \"one two\"& echo three" } });
        Assert.Equal(("\"one two\"", "three"), await done.Task.WaitAsync(TimeSpan.FromSeconds(60)));
        var file = Path.Combine(_dir, "my script.ps1");
        Assert.Equal("powershell.exe", BarScripts.StartInfo("\"" + file + "\"").FileName);
        Assert.Contains(file, BarScripts.StartInfo(file).ArgumentList);
    }

    [Fact]
    public void Commands_AreSplitFromTheirArguments()
    {
        var app = Path.Combine(_dir, "my app.exe");
        File.WriteAllText(app, "");
        Assert.Equal(("wt.exe", "-d C:\\"), TopBarHost.SplitCommand("wt.exe -d C:\\"));
        Assert.Equal((app, ""), TopBarHost.SplitCommand(app));
        Assert.Equal((app, "--now"), TopBarHost.SplitCommand(app + " --now"));
        Assert.Equal((app, "--now"), TopBarHost.SplitCommand($"\"{app}\" --now"));
        Assert.Equal(("https://example.org/a b", ""), TopBarHost.SplitCommand("https://example.org/a b"));
        Assert.Equal(("ms-settings:display", ""), TopBarHost.SplitCommand("ms-settings:display"));
    }

    // ---- Wallpapers ----

    private sealed class Hotkeys : IProfileHotkeys
    {
        public Dictionary<int, KeyGesture> Registered { get; } = new();
        public HashSet<Key> Taken { get; } = new();
        public bool Disposed { get; private set; }
        public event Action<int>? Pressed;
        public bool Register(int id, KeyGesture gesture) { if (Taken.Contains(gesture.Key)) return false; Registered[id] = gesture; return true; }
        public void Unregister(int id) => Registered.Remove(id);
        public void Press(int id) => Pressed?.Invoke(id);
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void WallpaperKeys_RunTheirAction_AndTellWhatIsTaken()
    {
        var made = new List<Hotkeys>();
        var ran = new List<WallpaperAction>();
        using var keys = new WallpaperKeys(ran.Add, () => { var next = new Hotkeys(); if (made.Count == 0) next.Taken.Add(Key.W); made.Add(next); return next; });
        var settings = new AppSettings();
        keys.Apply(settings);
        Assert.Contains("Ctrl+Alt+W", keys.Problem);
        made[0].Press((int)WallpaperAction.Next + 1);
        Assert.Equal(new[] { WallpaperAction.Next }, ran);
        keys.Apply(settings); // The key has become free.
        Assert.True(made[0].Disposed); Assert.Equal("", keys.Problem); Assert.Equal(3, made[1].Registered.Count);
        keys.Apply(settings);
        Assert.Equal(2, made.Count);
        settings.SwitcherKeys = ""; settings.NextWallpaperKeys = "nonsense";
        keys.Apply(settings);
        Assert.Single(made[2].Registered); Assert.Contains("nonsense", keys.Problem);
    }

    private string[] Pictures(string name, int count)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(folder);
        return Enumerable.Range(0, count).Select(i => { var path = Path.Combine(folder, $"{i:00}.png"); File.WriteAllBytes(path, new byte[] { 1 }); return path; }).ToArray();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 3000 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task Slideshow_CarriesOnFromTheChosenWallpaper()
    {
        var pictures = Pictures("show", 4);
        var applied = new ConcurrentQueue<string>();
        using var slideshow = new SlideshowController((path, _) => applied.Enqueue(path));
        slideshow.Start(new WallpaperProfile { FolderPath = Path.GetDirectoryName(pictures[0])!, SlideshowIntervalMinutes = 5 }, pictures[2]);
        await Until(() => applied.Count == 1);
        Assert.Equal(pictures[2], applied.Last());
        await slideshow.ShowAsync(pictures[1]);
        Assert.Equal(pictures[1], applied.Last());
        await (Task)typeof(SlideshowController).GetMethod("AdvanceAndApplyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(slideshow, null)!;
        Assert.Equal(pictures[2], applied.Last());
        await slideshow.StepAsync(-1); await slideshow.StepAsync(-1); await slideshow.StepAsync(-1);
        Assert.Equal(pictures[3], applied.Last()); // Around the beginning.
        // One that was added since, and one that is not the profile's.
        var added = Path.Combine(Path.GetDirectoryName(pictures[0])!, "99.png");
        File.WriteAllBytes(added, new byte[] { 1 });
        await slideshow.ShowAsync(added);
        Assert.Equal(added, applied.Last());
        var count = applied.Count;
        await slideshow.ShowAsync(Pictures("other", 1)[0]);
        Assert.Equal(count, applied.Count);
    }

    [Fact]
    public async Task ShuffledSlideshow_GoesBackToWhatWasShown()
    {
        var pictures = Pictures("shuffle", 6);
        var applied = new ConcurrentQueue<string>();
        using var slideshow = new SlideshowController((path, _) => applied.Enqueue(path));
        slideshow.Start(new WallpaperProfile { FolderPath = Path.GetDirectoryName(pictures[0])!, SlideshowIntervalMinutes = 5, SlideshowRandom = true });
        await Until(() => applied.Count == 1);
        await slideshow.StepAsync(1); await slideshow.StepAsync(1); await slideshow.ShowAsync(pictures[0]);
        var shown = applied.ToArray();
        await slideshow.StepAsync(-1);
        Assert.Equal(shown[2], applied.Last());
        await slideshow.StepAsync(-1);
        Assert.Equal(shown[1], applied.Last());
    }

    [Fact]
    public void Deck_LaysCardsOutAroundTheChosenOne()
    {
        var card = new Size(760, 400);
        foreach (var layout in new[] { SwitcherLayout.Slanted, SwitcherLayout.Carousel, SwitcherLayout.Filmstrip })
        {
            var look = SwitcherLook.From(new AppSettings { SwitcherLayout = layout });
            var middle = SwitcherDeck.Place(look, 0, card);
            var places = Enumerable.Range(0, 4).Select(i => SwitcherDeck.Place(look, i, card)).ToArray();
            Assert.Equal((0d, 1d), (middle.X, middle.Opacity));
            for (var i = 1; i < 4; i++)
            {
                Assert.True(places[i].X > places[i - 1].X); Assert.True(places[i].Order < places[i - 1].Order);
                Assert.True(places[i].Scale <= places[i - 1].Scale); Assert.True(places[i].Opacity <= places[i - 1].Opacity);
                Assert.Equal(-places[i].X, SwitcherDeck.Place(look, -i, card).X, 6);
            }
            Assert.Equal(0, places[3].Opacity, 6);
            // Nothing jumps on the way from one card to the next.
            for (var step = 0d; step < 2.9; step += .05)
            {
                var (here, next) = (SwitcherDeck.Place(look, step, card), SwitcherDeck.Place(look, step + .05, card));
                Assert.True(next.X - here.X is >= 0 and < 45, $"{layout} {step}");
                Assert.True(Math.Abs(next.Opacity - here.Opacity) < .06, $"{layout} {step}");
            }
        }
    }
}
