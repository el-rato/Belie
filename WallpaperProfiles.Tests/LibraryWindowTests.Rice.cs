using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using WallpaperProfiles.Workspaces;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed partial class LibraryWindowTests
{
    private static string? RiceExport => Environment.GetEnvironmentVariable("BELIE_TOPBAR_EXPORT") is { Length: > 0 } export ? export : null;

    private static void CheckRice(string dir)
    {
        CheckBarStyles();
        CheckIsland();
        CheckLooks(dir);
        CheckSwitcher(dir);
    }

    private static T ById<T>(FrameworkElement page, string id) where T : FrameworkElement => Within<T>(page).Single(item => AutomationProperties.GetAutomationId(item) == id);
    private static void Press(FrameworkElement page, string id) => ById<Button>(page, id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static void CheckBarStyles()
    {
        var bar = new TopBarWindow("test") { IsPreview = true };
        var settings = new TopBarSettings { WorkspaceCount = 3, WorkspaceLabels = WorkspaceLabelStyle.Pills, CenterStyle = { ["shape"] = "notch" }, Left = new(), Center = new(), Right = new() };
        settings.Left.Add(new BarModule(BarModuleKind.Workspaces));
        settings.Center.Add(new BarModule(BarModuleKind.Clock));
        var own = new BarModule(BarModuleKind.Text) { Text = "mail", Name = "mail", Icon = "M", Format = "{icon} <{value}>", OnClick = "mail.exe --new", Style = { ["color"] = "#112233", ["padding"] = "0 9px" } };
        settings.Right.Add(own);
        var script = new BarModule(BarModuleKind.Script) { Command = "echo", Icon = "-" };
        settings.Right.Add(script);
        settings.Right.Add(new BarModule(BarModuleKind.Wallpaper));
        settings.Css = "#custom-mail { border-radius: 7px; background: @accent; } #custom-mail:hover { background: red; } #nothing { wobble: 1; }";
        bar.Apply(settings.Normalize());
        Assert.Single(bar.StyleProblems);
        bar.UpdateWorkspaces(Enumerable.Range(1, 3).Select(index => new WorkspaceState(index, index == 2, 1)).ToArray());
        var content = (FrameworkElement)bar.Content;
        content.Measure(new Size(1200, 50)); content.Arrange(new Rect(0, 0, 1200, 40)); content.UpdateLayout();
        BarFrame FrameOf(string name)
        {
            DependencyObject item = Within<TextBlock>(content).First(text => text.Name == name);
            while (item is not BarFrame) item = LogicalTreeHelper.GetParent(item);
            return (BarFrame)item;
        }
        var mail = FrameOf("TopBarText");
        Assert.Equal(new CornerRadius(7), mail.CornerRadius);
        Assert.Equal(new Thickness(9, 0, 9, 0), mail.Padding);
        Assert.Equal(Color.FromRgb(0xDD, 0xF7, 0x9D), ((SolidColorBrush)mail.Background).Color);
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), ((SolidColorBrush)mail.GetValue(System.Windows.Documents.TextElement.ForegroundProperty)).Color);
        string? run = null;
        bar.LauncherClicked += command => run = command;
        mail.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        Assert.Equal("mail.exe --new", run);
        FrameOf("TopBarWallpaper").RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
        Assert.Equal("belie:next-wallpaper", run);
        bar.UpdateScript(script, "21 new", "from someone");
        Assert.Equal("21 new", Within<TextBlock>(content).Single(text => text.Name == "TopBarScript").Text);
        // The open workspace's dot is longer than the others.
        var buttons = Within<Button>(content).Where(button => AutomationProperties.GetName(button).StartsWith("Workspace")).ToArray();
        Assert.True(buttons[1].ActualWidth >= buttons[0].ActualWidth);
        bar.Close();
    }

    private static void CheckIsland()
    {
        var settings = new TopBarSettings { AnimationsEnabled = false, IslandOnWorkspace = true, VisualizerSkin = SpectrumSkin.Mirrored }.Normalize();
        var island = new BarIsland();
        island.Apply(settings, new BarLook { Background = Brushes.Black }, Brushes.Black, Brushes.White, Brushes.Orange, 30, preview: true);
        island.UpdateClock("21:41");
        var idle = island.Goal;
        Assert.Equal(30, idle.Height);
        var song = new WidgetStyles.SamplePlayer().ReadAsync(default).Result;
        island.UpdateMusic(song);
        Assert.Equal(IslandFace.Idle, island.Face); // Already playing when the bar started.
        Assert.True(island.Goal.Width > idle.Width);
        island.UpdateMusic(song with { Title = "Another" });
        Assert.Equal(IslandFace.Track, island.Face);
        Assert.Equal(new Size(settings.IslandWidth, 30 * settings.IslandGrowth), island.Goal);
        // Only the face that shows may listen to the music and draw it.
        static bool Seen(DependencyObject? item) => item is not UIElement element || element.Visibility == Visibility.Visible && Seen(LogicalTreeHelper.GetParent(item));
        Assert.Equal(2, Within<AudioSpectrumView>(island).Count());
        Assert.Single(Within<AudioSpectrumView>(island), view => Seen(view));
        island.UpdateVolume(40, false);
        Assert.Equal(IslandFace.Track, island.Face);
        island.UpdateVolume(44, false);
        Assert.Equal(IslandFace.Volume, island.Face);
        island.UpdateWorkspace(1, "1"); island.UpdateWorkspace(2, "Mail");
        Assert.Equal(IslandFace.Workspace, island.Face);

        // It reaches past the bar, which still reserves only its own height.
        var bar = new TopBarWindow("test") { IsPreview = true };
        var rice = BarRice.BuiltIn.Single(item => item.Name == "Island").ApplyTo(new TopBarSettings { AnimationsEnabled = false, WorkspaceCount = 5 });
        bar.Apply(rice, new WallpaperColors(Color.FromRgb(20, 24, 22), Color.FromRgb(240, 250, 244), Color.FromRgb(112, 190, 126)));
        bar.UpdateWorkspaces(Enumerable.Range(1, 5).Select(index => new WorkspaceState(index, index == 2, index % 2)).ToArray());
        bar.UpdateSystem(new SystemSnapshot(12, 48, 40, false, 88, false)); bar.UpdateMedia(song); bar.UpdateClock(new DateTime(2026, 11, 25, 21, 41, 0));
        var content = (FrameworkElement)bar.Content;
        bar.Content = null;
        var scene = new Grid { Width = 1280, Height = 110, Background = new LinearGradientBrush(Color.FromRgb(96, 130, 120), Color.FromRgb(150, 160, 120), 0) };
        scene.Children.Add(content);
        void Draw(string name)
        {
            scene.Measure(new Size(1280, 110)); scene.Arrange(new Rect(0, 0, 1280, 110)); scene.UpdateLayout();
            if (RiceExport is { } export) Capture(scene, Path.Combine(export, name));
        }
        Draw("island-idle.png");
        var live = Within<BarIsland>(content).Single();
        Assert.True(live.Frame.ActualHeight <= rice.Height);
        bar.UpdateMedia(song with { Title = "Orange Blossom", Artist = "Fogbank" });
        Draw("island-track.png");
        Assert.True(live.Frame.TranslatePoint(new Point(0, live.Frame.ActualHeight), scene).Y > rice.Height);
        Assert.Equal(640, live.Frame.TranslatePoint(new Point(live.Frame.ActualWidth / 2, 0), scene).X, 0);
        bar.Close();
    }

    private static void CheckLooks(string dir)
    {
        var folder = Path.Combine(dir, "rice");
        Directory.CreateDirectory(folder);
        var store = new TopBarStore(Path.Combine(folder, "topbar.json"));
        var profile = Guid.NewGuid();
        store.Save(new TopBarSettings { WorkspaceProfiles = new() { [1] = profile }, WorkspaceCount = 4, Height = 33 });
        store.SaveCss("#clock { color: red; }");
        var page = new TopBarPage(store, _ => { });
        Assert.Equal("#clock { color: red; }", ById<TextBox>(page, "TopBarCss").Text);
        while (page.DrawNext()) { }
        Assert.All(Within<Image>(page), picture => Assert.True(((BitmapSource)picture.Source).PixelWidth > 1000));

        Press(page, "LookMochapills");
        var saved = store.Load();
        var mocha = BarRice.BuiltIn.Single(rice => rice.Name == "Mocha pills");
        Assert.Equal(mocha.Css, saved.Css); Assert.Equal(mocha.Look.Height, saved.Height); Assert.Equal(profile, saved.WorkspaceProfiles[1]); Assert.Equal(4, saved.WorkspaceCount);
        Assert.Equal(mocha.Css, ById<TextBox>(page, "TopBarCss").Text.Replace("\r\n", "\n"));
        // What was there before can be had back.
        Press(page, "OwnLookPreviouslook");
        Assert.Equal("#clock { color: red; }", store.Load().Css); Assert.Equal(33, store.Load().Height);
        Press(page, "OwnLookPreviouslook");
        Assert.Equal(mocha.Css, store.Load().Css);
        page.SaveLook("Mine");
        Assert.True(File.Exists(Path.Combine(folder, "bar-looks", "Mine.bar.json")));

        // A look from somebody else keeps its commands only when that is allowed.
        var theirs = new BarRice { Name = "Theirs", Look = new TopBarSettings { Right = new() { new BarModule(BarModuleKind.Script) { Command = "evil" } } } };
        var path = Path.Combine(dir, "theirs.bar.json");
        BarRiceStore.Write(path, theirs);
        var asked = 0;
        page.AskAboutCommands = rice => { asked++; Assert.Equal(new[] { "evil" }, rice.Commands); return null; };
        page.ImportLook(path);
        Assert.False(File.Exists(Path.Combine(folder, "bar-looks", "Theirs.bar.json")));
        page.AskAboutCommands = _ => { asked++; return false; };
        page.ImportLook(path);
        Assert.Equal(2, asked);
        Assert.Empty(BarRiceStore.Read(Path.Combine(folder, "bar-looks", "Theirs.bar.json")).Commands);
        File.WriteAllText(path, "nonsense");
        page.ImportLook(path);

        // The editor of one module, of a part, and the stylesheet.
        page.AddModule(BarModuleKind.Script, 2);
        var module = page.Modules(2)[^1];
        page.Edit(module);
        ById<TextBox>(page, "ModuleCommand").Text = "echo mail"; ById<TextBox>(page, "ModuleInterval").Text = "30";
        ById<TextBox>(page, "ModuleColor").Text = "@accent"; ById<TextBox>(page, "ModuleBackground").Text = "#12345";
        ById<TextBox>(page, "ModuleRightClick").Text = "belie:wallpapers"; ById<TextBox>(page, "ModuleName").Text = "my mail";
        ById<ComboBox>(page, "ModuleFontFamily").SelectedItem = BarFonts.Names().First(name => name == "Consolas");
        ById<ComboBox>(page, "TopBarPart").SelectedIndex = 2;
        ById<TextBox>(page, "PartShape").Text = "notch";
        ById<TextBox>(page, "TopBarCss").Text = "bar { padding: 3px; }";
        Assert.True(page.SaveNow());
        saved = store.Load();
        var script = saved.Right![^1];
        Assert.Equal(("echo mail", 30, "belie:wallpapers", "mymail"), (script.Command, script.Interval, script.OnRightClick, script.Name));
        Assert.Equal(new Dictionary<string, string> { ["color"] = "@accent", ["font-family"] = "Consolas" }, script.Style);
        Assert.Equal("notch", saved.CenterStyle["shape"]); Assert.Equal("bar { padding: 3px; }", saved.Css);
        // Every kind of module can be edited.
        foreach (var kind in Enum.GetValues<BarModuleKind>()) page.Edit(new BarModule(kind));
        page.Reload();

        if (RiceExport is not { } export) return;
        foreach (var rice in BarRice.BuiltIn)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BarPreview.Render(rice.ApplyTo(new TopBarSettings()), new WallpaperColors(Color.FromRgb(24, 22, 28), Color.FromRgb(250, 240, 244), Color.FromRgb(236, 140, 150)), null)));
            using var output = File.Create(Path.Combine(export, "look-" + rice.Name + ".png"));
            encoder.Save(output);
        }
        var window = new Window { Content = page, Width = 860, Height = 900, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        window.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "BgBrush");
        window.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
        window.Show();
        while (page.DrawNext()) { }
        page.Edit(page.Modules(2)[^1]);
        var tabs = Within<TabControl>(page).Single();
        for (var tab = 0; tab < tabs.Items.Count; tab++)
        {
            tabs.SelectedIndex = tab;
            PumpUntil(() => true); window.UpdateLayout();
            if (tab == 1) { Within<ScrollViewer>(page).First(scroll => scroll.IsVisible).ScrollToVerticalOffset(330); PumpUntil(() => true); window.UpdateLayout(); }
            Capture(page, Path.Combine(export, $"rice-page-{tab}.png"));
        }
        window.Close();
    }

    private static BitmapSource Scenery(int index)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var hue = index * 47 % 360;
            Color Of(double light) { var c = (1 - Math.Abs(2 * light - 1)) * .55; var x = c * (1 - Math.Abs(hue / 60d % 2 - 1)); var m = light - c / 2;
                var (r, g, b) = (hue / 60) switch { 0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x) };
                return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255)); }
            context.DrawRectangle(new LinearGradientBrush(Of(.3), Of(.75), 90), null, new Rect(0, 0, 640, 360));
            context.DrawEllipse(new SolidColorBrush(Of(.88)), null, new Point(120 + index * 70 % 400, 110), 46, 46);
            for (var hill = 0; hill < 3; hill++) context.DrawEllipse(new SolidColorBrush(Of(.34 - hill * .09)), null, new Point(120 + hill * 220, 400 + hill * 16), 260, 150);
        }
        var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    private static void CheckSwitcher(string dir)
    {
        var items = Enumerable.Range(0, 9).Select(i => $@"C:\none\{i}.png").ToArray();
        var pictures = Enumerable.Range(0, 9).Select(Scenery).ToArray();
        var deck = new SwitcherDeck { Read = (path, _) => pictures[int.Parse(Path.GetFileNameWithoutExtension(path))], Aspect = 16 / 9d };
        var scene = new Grid { Width = 1280, Height = 720, Background = new ImageBrush(pictures[0]), ClipToBounds = true };
        scene.Children.Add(deck);
        void Layout() { scene.Measure(new Size(1280, 720)); scene.Arrange(new Rect(0, 0, 1280, 720)); scene.UpdateLayout(); }
        Layout();
        string? chosen = null;
        deck.Chosen += path => chosen = path;
        var look = new AppSettings { SwitcherSpeed = 0 }; // Without motion, so that places are final.
        deck.Apply(SwitcherLook.From(look), Brushes.Black);
        deck.Show(items, items[3]);
        Assert.Equal(items[3], deck.SelectedPath);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, deck.Cards.Keys.OrderBy(index => index));
        deck.MoveBy(-4); // Around the beginning.
        Assert.Equal(items[8], deck.SelectedPath);
        Assert.Equal(new[] { 0, 1, 6, 7, 8 }, deck.Cards.Keys.OrderBy(index => index));
        deck.Select(0); deck.Choose();
        Assert.Equal(items[0], chosen);
        deck.Show(items, items[4].ToUpperInvariant());
        Assert.Equal(4, deck.Selected);
        Layout();
        PumpUntil(() => deck.Cards.Values.All(card => card.HasImage));
        foreach (var layout in Enum.GetValues<SwitcherLayout>())
        {
            look.SwitcherLayout = layout; look.SwitcherRadius = layout == SwitcherLayout.Slanted ? 0 : 12; look.SwitcherSlant = layout is SwitcherLayout.Slanted or SwitcherLayout.Filmstrip ? 18 : 0;
            look.SwitcherSpacing = layout == SwitcherLayout.Slanted ? 0 : 12;
            deck.Apply(SwitcherLook.From(look), new SolidColorBrush(Color.FromArgb(220, 12, 13, 16)));
            deck.MoveRows(1); deck.MoveRows(-1);
            Layout();
            PumpUntil(() => deck.Cards.Values.All(card => card.HasImage));
            Assert.Equal(4, deck.Selected);
            if (RiceExport is { } export) Capture(scene, Path.Combine(export, $"switcher-{layout}.png"));
        }
        // Few wallpapers do not go around, but past the end is still the beginning.
        look.SwitcherLayout = SwitcherLayout.Slanted;
        deck.Apply(SwitcherLook.From(look), Brushes.Black);
        deck.Show(items.Take(3).ToArray(), items[2]);
        Assert.Equal(3, deck.Cards.Count);
        deck.MoveBy(1);
        Assert.Equal(items[0], deck.SelectedPath);
        deck.MoveBy(5);
        Assert.Equal(items[2], deck.SelectedPath);
        deck.Show(Array.Empty<string>(), "");
        chosen = null; deck.MoveBy(1); deck.Select(3); deck.Choose();
        Assert.Null(chosen); Assert.Equal("", deck.SelectedPath);
        deck.Close();

        // The window itself is never shown here.
        var first = new WallpaperProfile { Name = "First" }; var second = new WallpaperProfile { Name = "Second" };
        (Guid, string)? picked = null;
        var settings = new AppSettings { ChosenWallpapers = { [second.Id] = items[5] } };
        var switcher = new WallpaperSwitcher(settings, new[] { first, second }, first, (id, path) => picked = (id, path), new System.Drawing.Rectangle(0, 0, 1920, 1080),
            profile => profile == first ? items.Take(4).ToArray() : items.Skip(4).ToArray());
        switcher.ChangeProfile(1);
        PumpUntil(() => switcher.Deck.Items.Count == 5);
        Assert.Equal(second, switcher.Profile); Assert.Equal(items[5], switcher.Deck.SelectedPath);
        Assert.Null(picked);
        switcher.Deck.MoveBy(1); switcher.Deck.Choose();
        Assert.Equal((second.Id, items[6]), picked);

        var opened = 0; var problem = "";
        var store = new SettingsStore(Path.Combine(dir, "switcher-settings.json"));
        var page = new SwitcherPage(store, settings, () => (items, items[0]), () => opened++, () => problem);
        ById<ComboBox>(page, "SwitcherLayout").SelectedIndex = 1;
        Assert.True(page.SaveNow());
        Assert.Equal(SwitcherLayout.Carousel, store.Load().SwitcherLayout);
        Assert.Equal(items[5], store.Load().ChosenWallpapers[second.Id]);
    }
}
