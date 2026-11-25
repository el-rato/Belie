using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace WallpaperProfiles.Tests;

public sealed class WallpaperTransitionTests
{
    private readonly ITestOutputHelper _output;
    public WallpaperTransitionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ColoniesCoverEveryCellOnceAndAreRepeatable()
    {
        var field = TransitionField.Colonies(160, 90, 7);
        Assert.Equal(Enumerable.Range(0, 160 * 90), field.Order.OrderBy(cell => cell));
        Assert.All(field.Arrival, time => Assert.InRange(time, 0, 1.0001f));
        for (var i = 1; i < field.Order.Length; i++) Assert.True(field.Arrival[field.Order[i - 1]] <= field.Arrival[field.Order[i]]);
        Assert.Equal(field.Arrival, TransitionField.Colonies(160, 90, 7).Arrival);
        Assert.NotEqual(field.Arrival, TransitionField.Colonies(160, 90, 8).Arrival);
        // It spreads steadily instead of finishing in a rush.
        var half = field.Arrival.Count(time => time <= .5f) / (float)field.Arrival.Length;
        Assert.InRange(half, .25f, .75f);
    }

    [Fact]
    public void OutlineFillsTheSubjectBeforeSpreading()
    {
        var mask = Blob(160, 90);
        var field = TransitionField.Outline(160, 90, mask, 3);
        Assert.NotEmpty(field.Lines);
        Assert.Equal(Enumerable.Range(0, mask.Length), field.Order.OrderBy(cell => cell));
        Assert.All(field.Arrival, time => Assert.InRange(time, TransitionField.OutlineEnd, 1.0001f));
        var inside = Enumerable.Range(0, mask.Length).Where(cell => mask[cell]).Average(cell => field.Arrival[cell]);
        var outside = Enumerable.Range(0, mask.Length).Where(cell => !mask[cell]).Average(cell => field.Arrival[cell]);
        Assert.True(inside < outside);
        // The line runs along the subject's edge.
        Assert.All(field.Lines.SelectMany(line => line), point =>
            Assert.InRange(Math.Sqrt(Math.Pow(point.X - 80, 2) + Math.Pow(point.Y - 45, 2)), 22, 28));
    }

    [Fact]
    public void FindsAClearSubjectAndNothingInScenery()
    {
        var random = new Random(1);
        var subject = Blob(192, 108);
        var portrait = subject.Select(inside => Pixel(random, inside ? Color.Firebrick : Color.FromArgb(24, 34, 52))).ToArray();
        var found = SubjectFinder.Find(portrait, 192, 108);
        Assert.NotNull(found);
        Assert.True(found!.Zip(subject).Count(pair => pair.First != pair.Second) < subject.Length * .02);
        Assert.Equal(160 * 90, SubjectFinder.Scale(found, 192, 108, 160, 90).Length);

        var landscape = Enumerable.Range(0, 192 * 108).Select(i =>
            Pixel(random, i / 192 < 40 + 14 * Math.Sin(i % 192 / 17.0) ? Color.SkyBlue : i / 192 < 70 ? Color.ForestGreen : Color.SaddleBrown)).ToArray();
        Assert.Null(SubjectFinder.Find(landscape, 192, 108));
        Assert.Null(SubjectFinder.Find(Enumerable.Range(0, 192 * 108).Select(_ => random.Next(0x1000000)).ToArray(), 192, 108));
        Assert.Null(SubjectFinder.Find(new int[192 * 108], 192, 108));
    }

    [Theory]
    [InlineData(FitMode.Fill, 0, -60, 1920, 1200)]
    [InlineData(FitMode.Fit, 96, 0, 1728, 1080)]
    [InlineData(FitMode.Stretch, 0, 0, 1920, 1080)]
    [InlineData(FitMode.Center, 560, 290, 800, 500)]
    [InlineData(FitMode.Span, 0, -660, 3840, 2400)]
    public void PlacesPicturesWhereWindowsDoes(FitMode fit, int x, int y, int width, int height)
        => Assert.Equal(new Rectangle(x, y, width, height), WallpaperTransition.Place(new Size(800, 500), fit,
            new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 3840, 1080)));

    [Fact]
    public void GlowUsesTheStrongestColor()
    {
        var pixels = Enumerable.Range(0, 1000).Select(i => i < 900 ? 0x202428 : 0xB01020).ToArray();
        var glow = WallpaperTransition.Vivid(pixels);
        Assert.True(glow.R > 200 && glow.G < 60 && glow.B < 80, glow.ToString());
        Assert.Equal(0x80, WallpaperTransition.Vivid(Enumerable.Repeat(0x808080, 100).ToArray()).R);
    }

    [Fact]
    public async Task WithoutATransitionTheWallpaperIsSetDirectly()
    {
        using var transition = new WallpaperTransition();
        var applied = 0;
        Assert.True(await transition.ChangeAsync("missing.png", FitMode.Fill, WallpaperTransitionStyle.None, () => { applied++; return Task.FromResult(true); }));
        Assert.False(await transition.ChangeAsync("missing.png", FitMode.Fill, WallpaperTransitionStyle.Colonies, () => { applied++; return Task.FromResult(false); }));
        Assert.Equal(2, applied);
        Assert.False(transition.IsPlaying);
    }

    [Fact]
    public void SettingIsRememberedAndRepaired()
    {
        var file = Path.Combine(Path.GetTempPath(), "belie-transition-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(file);
            Assert.Equal(WallpaperTransitionStyle.Automatic, store.Load().WallpaperTransition);
            store.Save(new AppSettings { WallpaperTransition = WallpaperTransitionStyle.None });
            Assert.Equal(WallpaperTransitionStyle.None, store.Load().WallpaperTransition);
            store.Save(new AppSettings { WallpaperTransition = (WallpaperTransitionStyle)42 });
            Assert.Equal(WallpaperTransitionStyle.Automatic, store.Load().WallpaperTransition);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheOldWallpaperDarkensWhileTheOutlineIsDrawn()
    {
        const int width = 320, height = 180;
        var field = TransitionField.Outline(160, 90, Blob(160, 90), 3);
        using var next = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(next)) graphics.Clear(Color.FromArgb(200, 100, 50));
        var pixels = Marshal.AllocHGlobal(width * height * 4);
        try
        {
            int Corner() => Marshal.ReadInt32(pixels) & 0xFFFFFF;
            void Start(TransitionPainter painter, double dim)
            {
                for (var i = 0; i < width * height; i++) Marshal.WriteInt32(pixels, i * 4, 0x80C040);
                painter.Begin(next, field, Color.White, Color.White, new TransitionOptions(WallpaperTransitionStyle.Automatic, Cell: 2, Dim: dim));
            }
            using var painter = new TransitionPainter(pixels, width, height);
            Start(painter, .5);
            Assert.Equal(new Rectangle(0, 0, width, height), painter.Draw(.1f));
            Assert.InRange(Corner() >> 8 & 255, 0x61, 0xBF);
            painter.Draw(.3f);
            Assert.Equal(0x406020, Corner());
            // The line survives the darkening, and all of it is drawn.
            var white = Enumerable.Range(0, width * height).Count(i => (Marshal.ReadInt32(pixels, i * 4) & 0xFFFFFF) == 0xFFFFFF);
            painter.Draw(.4f);
            Assert.True(white > 100, white.ToString());
            Assert.Equal(0x406020, Corner()); // It stays dark until its turn comes.
            painter.Draw(1f);
            Assert.All(Enumerable.Range(0, width * height), i => Assert.Equal(0xC86432, Marshal.ReadInt32(pixels, i * 4) & 0xFFFFFF));

            Start(painter, 0);
            painter.Draw(.3f);
            Assert.Equal(0x80C040, Corner());
            painter.Detach();
        }
        finally { Marshal.FreeHGlobal(pixels); }
    }

    [Fact]
    public void TilesHideTheDesktopTogether()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var hidden = new bool[96 * 54];
        DesktopLayer.Cover(hidden, 96, 54, screen, new Rectangle(6, 44, 951, 982));
        Assert.False(DesktopLayer.Covered(hidden));
        DesktopLayer.Cover(hidden, 96, 54, screen, new Rectangle(963, 44, 951, 488));
        DesktopLayer.Cover(hidden, 96, 54, screen, new Rectangle(963, 538, 951, 488));
        Assert.True(DesktopLayer.Covered(hidden));
        DesktopLayer.Cover(hidden, 96, 54, screen, new Rectangle(-500, -500, 100, 100)); // Off the screen.
    }

    private static bool[] Blob(int width, int height) => Enumerable.Range(0, width * height)
        .Select(i => Math.Pow(i % width - width / 2 + .5, 2) + Math.Pow(i / width - height / 2 + .5, 2) < 25 * 25).ToArray();

    private static int Pixel(Random random, Color color)
    {
        int Near(int value) => Math.Clamp(value + random.Next(-8, 9), 0, 255);
        return Near(color.R) << 16 | Near(color.G) << 8 | Near(color.B);
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? type, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint size, string value, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);

    private static IntPtr Below(IntPtr window)
    {
        do window = GetWindow(window, 2); while (window != IntPtr.Zero && !IsWindowVisible(window));
        return window;
    }

    /// <summary>
    /// Plays a real transition above the desktop for two seconds without touching the wallpaper setting.
    /// BELIE_TRANSITION_IMAGE picks the picture, BELIE_TRANSITION_FRAMES a folder for screenshots.
    /// </summary>
    [Fact]
    [Trait("Category", "Desktop")]
    public async Task PlaysAboveTheDesktop()
    {
        if (Environment.GetEnvironmentVariable("BELIE_DESKTOP_TESTS") != "1") return;
        var directory = Path.Combine(Path.GetTempPath(), "belie-transition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Logger.Init(directory);
        try
        {
            var path = Environment.GetEnvironmentVariable("BELIE_TRANSITION_IMAGE");
            if (path == null)
            {
                path = Path.Combine(directory, "next.png");
                using var picture = new Bitmap(1280, 720);
                using (var graphics = Graphics.FromImage(picture)) { graphics.Clear(Color.Navy); graphics.FillEllipse(Brushes.Orange, 440, 200, 400, 400); }
                picture.Save(path);
            }
            var before = WallpaperTransition.CurrentWallpaper();
            using var transition = new WallpaperTransition { EvenWhenCovered = true };
            var preparing = Stopwatch.StartNew();
            var appliedAfter = 0.0;
            var change = transition.ChangeAsync(path, FitMode.Fill, WallpaperTransitionStyle.Automatic,
                () =>
                {
                    appliedAfter = preparing.Elapsed.TotalMilliseconds;
                    SystemParametersInfo(0x14, 0, before.Path, 0); // Sets the same picture again, so that Explorer repaints.
                    return Task.FromResult(true);
                });
            IntPtr window;
            while ((window = FindWindow(null, WallpaperTransition.WindowTitle)) == IntPtr.Zero && preparing.Elapsed.TotalSeconds < 5) await Task.Delay(10);
            Assert.NotEqual(IntPtr.Zero, window);
            var shownAfter = preparing.Elapsed.TotalMilliseconds;
            await Task.Delay(30); // It is found before it has been put in its place.
            Assert.Equal(UI.DesktopWidgetWindow.FindDesktop(), Below(window)); // Directly above the desktop.
            Assert.True(await change);
            await Task.Delay(300);
            Assert.Equal(UI.DesktopWidgetWindow.FindDesktop(), Below(window));
            if (Environment.GetEnvironmentVariable("BELIE_TRANSITION_FRAMES") is { } frames)
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using var shot = new Bitmap(screen.Width, screen.Height);
                foreach (var at in new[] { 300, 600, 900, 1200, 1600 })
                {
                    await Task.Delay(Math.Max(0, (int)(shownAfter + at - preparing.Elapsed.TotalMilliseconds)));
                    using var graphics = Graphics.FromImage(shot);
                    var context = graphics.GetHdc();
                    PrintWindow(window, context, 2);
                    graphics.ReleaseHdc(context);
                    shot.Save(Path.Combine(frames, Path.GetFileNameWithoutExtension(path) + "-" + at + ".png"));
                }
            }
            while (transition.IsPlaying && preparing.Elapsed.TotalSeconds < 8) await Task.Delay(20);
            Assert.False(transition.IsPlaying);
            Assert.Equal(IntPtr.Zero, FindWindow(null, WallpaperTransition.WindowTitle));
            Assert.Equal(before, WallpaperTransition.CurrentWallpaper());
            var (count, time) = transition.LastRun;
            _output.WriteLine($"Shown after {shownAfter:0} ms, wallpaper set after {appliedAfter:0} ms, {count} frames in {time:0} ms ({count * 1000 / time:0} fps)");

            // A change that arrives halfway carries on in the same window.
            var first = transition.ChangeAsync(path, FitMode.Fill, WallpaperTransitionStyle.Colonies, () => Task.FromResult(true));
            while ((window = FindWindow(null, WallpaperTransition.WindowTitle)) == IntPtr.Zero) await Task.Delay(10);
            await Task.Delay(700);
            var second = transition.ChangeAsync(before.Path, FitMode.Fit, WallpaperTransitionStyle.Colonies, () => Task.FromResult(true));
            Assert.True(await first);
            Assert.True(await second);
            Assert.Equal(window, FindWindow(null, WallpaperTransition.WindowTitle));
            // A wallpaper that could not be set leaves the old one showing.
            while (transition.IsPlaying) await Task.Delay(20);
            Assert.False(await transition.ChangeAsync(path, FitMode.Fill, WallpaperTransitionStyle.Colonies, () => Task.FromResult(false)));
            var stopping = Stopwatch.StartNew();
            while (transition.IsPlaying) await Task.Delay(20);
            Assert.True(stopping.ElapsedMilliseconds < 1000);
            Assert.DoesNotContain(Directory.GetFiles(directory, "*.log").Select(File.ReadAllText), text => text.Contains("transition"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
