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
    private static void CheckTopBar(string dir)
    {
        CheckTopBarSettings(dir);
        // Never shown, so the test run does not reserve space on the real desktop.
        var bar = new TopBarWindow("test");
        var settings = new TopBarSettings { WorkspacesEnabled = true, WorkspaceCount = 3, ShowIcons = false };
        bar.Apply(settings);
        bar.UpdateWorkspaces(new[] { new WorkspaceState(1, true, 1), new WorkspaceState(2, false, 2), new WorkspaceState(3, false, 0) });
        bar.UpdateTitle("Notes"); bar.UpdateMedia(null);
        bar.UpdateSystem(new SystemSnapshot(12, 48, 40, false, 88, false));
        var content = (FrameworkElement)bar.Content;
        var buttons = Within<Button>(content).ToArray();
        Assert.True(buttons.Length >= 3);
        Assert.Equal("Workspace 1, 1 window, open", AutomationProperties.GetName(buttons[0]));
        Assert.Equal("Workspace 2, 2 windows", AutomationProperties.GetName(buttons[1]));
        int? chosen = null;
        bar.WorkspaceChosen += index => chosen = index;
        buttons[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(3, chosen);
        string Text(string name) => Within<TextBlock>(content).Single(text => text.Name == name).Text;
        Assert.Equal("Notes", Text("TopBarTitle"));
        Assert.Equal("CPU 12%", Text("TopBarCpu"));
        Assert.Equal("Vol 40%", Text("TopBarVolume"));
        Assert.Equal("Bat 88%", Text("TopBarBattery"));
        bar.UpdateSystem(new SystemSnapshot(0, 0, 40, true, null, true));
        Assert.Equal("Muted", Text("TopBarVolume"));
        static bool Hidden(DependencyObject? item) => item is UIElement element and not Window && (element.Visibility != Visibility.Visible || Hidden(LogicalTreeHelper.GetParent(item)));
        Assert.True(Hidden(Within<TextBlock>(content).Single(text => text.Name == "TopBarBattery")));
        Assert.False(Hidden(Within<TextBlock>(content).Single(text => text.Name == "TopBarVolume")));
        settings.WorkspaceCount = 2; settings.WorkspaceLabels = WorkspaceLabelStyle.Dots; bar.Apply(settings);
        Assert.Equal(2, Within<Button>(content).Count());
        // Every module, in every style, with every kind of label.
        var every = new TopBarSettings { WorkspacesEnabled = true, WorkspaceCount = 4, WorkspaceNames = new() { "Web", "Code" }, Left = new(), Center = new(), Right = new() };
        foreach (var kind in Enum.GetValues<BarModuleKind>())
            (kind <= BarModuleKind.Title ? every.Left : kind <= BarModuleKind.Date ? every.Center : every.Right).Add(new BarModule(kind) { Text = "Files", Command = "explorer.exe" });
        foreach (var style in Enum.GetValues<TopBarStyle>())
        {
            every.Style = style; every.BarMargin = style == TopBarStyle.Solid ? 0 : 6; every.BorderWidth = style == TopBarStyle.Pills ? 1 : 0;
            every.MediaStyle = (BarMediaStyle)(int)style; every.WorkspaceLabels = (WorkspaceLabelStyle)((int)style + 1);
            bar.Apply(every.Normalize());
            bar.UpdateWorkspaces(Enumerable.Range(1, 4).Select(index => new WorkspaceState(index, index == 1, index % 3)).ToArray());
            bar.UpdateTitle("Notes"); bar.UpdateClock(new DateTime(2026, 11, 25, 9, 30, 0));
            bar.UpdateSystem(new SystemSnapshot(12, 48, 40, false, 88, false, 61, 2_400_000, 120_000, TimeSpan.FromHours(5)));
            bar.UpdateWeather(new WeatherReport("Lisbon", 21, 20, 56, 12, 2, true, false, Array.Empty<WeatherDay>()));
            bar.UpdateMedia(new WidgetStyles.SamplePlayer().ReadAsync(default).Result);
            content.Measure(new Size(1900, 64)); content.Arrange(new Rect(0, 0, 1900, 50)); content.UpdateLayout();
            // The island shows one thing at a time, and here that is the volume.
            foreach (var kind in Enum.GetValues<BarModuleKind>().Except(new[] { BarModuleKind.Workspaces, BarModuleKind.Visualizer, BarModuleKind.Island }))
                Assert.False(Hidden(Within<TextBlock>(content).FirstOrDefault(text => text.Name == "TopBar" + kind) ?? (DependencyObject)content), kind.ToString());
            if (Environment.GetEnvironmentVariable("BELIE_TOPBAR_EXPORT") is { Length: > 0 } export) Capture(content, Path.Combine(export, $"live-{style}.png"));
            // The sides take the room the middle leaves, and what does not fit is left out whole.
            foreach (var width in new[] { 2600d, 1900, 1500, 900, 300 })
            {
                content.Measure(new Size(width, 64)); content.Arrange(new Rect(0, 0, width, 50)); content.UpdateLayout();
                var rows = Within<BarRow>(content).ToArray();
                foreach (var row in rows)
                {
                    if (width == 2600) Assert.True(row.Omitted.Count == 0, $"{style} {row.ActualWidth} of {row.Firm}: " + string.Join(", ", row.Children.Cast<FrameworkElement>().Select(item => Within<TextBlock>(item).LastOrDefault()?.Name + " " + item.DesiredSize.Width)));
                    foreach (var module in row.Children.Cast<FrameworkElement>().Where(item => item.Visibility == Visibility.Visible).Except(row.Omitted.Cast<FrameworkElement>()))
                        Assert.True(module.TranslatePoint(new Point(module.ActualWidth, 0), row).X <= row.ActualWidth + 2, $"{style} {width}");
                }
                if (width == 300) Assert.NotEmpty(rows[^1].Omitted);
            }
        }
        Assert.Equal(System.Windows.Media.Brushes.Black, TopBarWindow.InkFor(Colors.White));
        Assert.Equal(System.Windows.Media.Brushes.White, TopBarWindow.InkFor(Colors.Navy));
        bar.Close();
    }

    private static void CheckTopBarSettings(string dir)
    {
        var store = new TopBarStore(Path.Combine(dir, "topbar.json"));
        var profile = new WallpaperProfile { Name = "Focus" };
        var started = 0;
        var bar = new TopBarPage(store, _ => started++);
        var spaces = new WorkspacesPage(store, () => new[] { profile }, _ => started++);
        T Find<T>(FrameworkElement page, string id) where T : FrameworkElement => Within<T>(page).Single(item => AutomationProperties.GetAutomationId(item) == id);
        Assert.True(bar.SaveNow());
        Assert.False(store.Load().Enabled);
        Assert.Equal(0, started);
        Find<CheckBox>(bar, "TopBarEnabled").IsChecked = true;
        Assert.True(bar.SaveNow());
        Assert.Equal(1, started);
        // Each is optional: the bar does not bring workspaces along, and workspaces do not need the bar.
        Assert.False(store.Load().WorkspacesEnabled);
        Find<CheckBox>(bar, "TopBarEnabled").IsChecked = false; Assert.True(bar.SaveNow());
        Assert.Equal(1, started); Assert.False(store.Load().Runs);
        Find<CheckBox>(spaces, "TopBarWorkspaces").IsChecked = true; Assert.True(spaces.SaveNow());
        Assert.Equal(2, started); Assert.False(store.Load().Enabled); Assert.True(store.Load().WorkspacesEnabled);
        Find<CheckBox>(bar, "TopBarEnabled").IsChecked = true; Assert.True(bar.SaveNow());
        Assert.True(store.Load().WorkspacesEnabled);

        Find<ComboBox>(spaces, "TopBarWorkspaceCount").SelectedIndex = 3;
        Find<ComboBox>(spaces, "TopBarShortcuts").SelectedIndex = 2;
        Find<ComboBox>(spaces, "TopBarProfile2").SelectedIndex = 1;
        Find<TextBox>(spaces, "TopBarName2").Text = "Mail";
        Assert.True(Find<CheckBox>(spaces, "TopBarTiling").IsChecked); Assert.True(Find<CheckBox>(spaces, "TopBarAnimations").IsChecked);
        Find<CheckBox>(spaces, "TopBarTiling").IsChecked = false;
        Find<CheckBox>(spaces, "TopBarSmartGaps").IsChecked = true;
        Find<Slider>(spaces, "TopBarTileGap").Value = 12; Find<Slider>(spaces, "TopBarTileMargin").Value = 900;
        Find<Slider>(spaces, "TopBarSplitRatio").Value = 60;
        Find<CheckBox>(spaces, "TopBarAnimations").IsChecked = false;
        Find<ComboBox>(spaces, "TopBarMotion").SelectedIndex = 1;
        Find<Slider>(spaces, "TopBarAnimationSpeed").Value = 150;
        Find<TextBox>(spaces, "TopBarPinnedApps").Text = "Spotify.exe, Discord";
        Find<TextBox>(spaces, "TopBarFloatingApps").Text = "obs64.exe";
        Assert.True(spaces.SaveNow());
        var saved = store.Load();
        Assert.False(saved.TilingEnabled); Assert.False(saved.AnimationsEnabled); Assert.True(saved.SmartGaps);
        Assert.Equal(12, saved.TileGap); Assert.Equal(64, saved.TileMargin); Assert.Equal(1.5, saved.AnimationSpeed); Assert.Equal(.6, saved.SplitRatio, 6);
        Assert.True(saved.Enabled); Assert.Equal(4, saved.WorkspaceCount);
        Assert.Equal(WorkspaceShortcutModifier.None, saved.Shortcuts); Assert.Equal(WorkspaceMotion.Vertical, saved.Motion);
        Assert.Equal(profile.Id, saved.WorkspaceProfiles[2]); Assert.Equal("Mail", saved.WorkspaceName(2));
        Assert.Equal(new[] { "Spotify", "Discord" }, saved.PinnedApps); Assert.Equal(new[] { "obs64" }, saved.FloatingApps);

        // The other page was open all along and must not undo any of that.
        Find<TextBox>(bar, "TopBarAccent").Text = "pink"; Find<Slider>(bar, "TopBarHeight").Value = 44;
        Assert.False(bar.SaveNow());
        Assert.Equal("#DDF79D", store.Load().AccentColor); Assert.Equal(44, store.Load().Height);
        Find<TextBox>(bar, "TopBarAccent").Text = "#FF88AA";
        Assert.True(bar.SaveNow());
        Assert.Equal("#FF88AA", store.Load().AccentColor); Assert.Equal(4, store.Load().WorkspaceCount); Assert.Equal("Mail", store.Load().WorkspaceName(2));
        Find<TextBox>(bar, "TopBarClockFormat").Text = "%";
        Assert.False(bar.SaveNow());
        Find<TextBox>(bar, "TopBarClockFormat").Text = "HH:mm:ss";
        Assert.True(bar.SaveNow());
        Find<Button>(bar, "TopBarPaletteNord").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.True(bar.SaveNow());
        Assert.Equal("#88C0D0", store.Load().AccentColor);

        // Modules: add, move along and into the next part, and start over from a set.
        Assert.DoesNotContain(bar.Available(), item => item.Kind == BarModuleKind.Clock);
        bar.AddModule(BarModuleKind.Weather, 0); bar.AddModule(BarModuleKind.Text, 0); bar.AddModule(BarModuleKind.Text, 0);
        Assert.DoesNotContain(bar.Available(), item => item.Kind == BarModuleKind.Weather);
        Assert.Contains(bar.Available(), item => item.Kind == BarModuleKind.Text);
        bar.Move(0, 2, -1); bar.Move(0, 4, 1);
        Assert.True(bar.SaveNow());
        Assert.Equal(new[] { BarModuleKind.Workspaces, BarModuleKind.Weather, BarModuleKind.Title, BarModuleKind.Text }, store.Load().Left!.Select(module => module.Kind));
        Assert.Equal(new[] { BarModuleKind.Text, BarModuleKind.Clock }, store.Load().Center!.Select(module => module.Kind));
        Find<Button>(bar, "TopBarSetEssentials").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.True(bar.SaveNow());
        Assert.Equal(new[] { BarModuleKind.Volume, BarModuleKind.Battery }, store.Load().Right!.Select(module => module.Kind));
        Find<Button>(bar, "TopBarSetEverything").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.True(bar.SaveNow());

        var settings = new SettingsStore(Path.Combine(dir, "transition-settings.json"));
        var chosen = new AppSettings();
        var transitions = new TransitionsPage(settings, chosen, () => Array.Empty<string>());
        Find<Button>(transitions, "TransitionWave").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Find<Slider>(transitions, "TransitionSeconds").Value = 3; Find<Slider>(transitions, "TransitionCell").Value = 12; Find<Slider>(transitions, "TransitionAngle").Value = 90;
        Find<CheckBox>(transitions, "TransitionGlow").IsChecked = false;
        Find<TextBox>(transitions, "TransitionGlowColor").Text = "#FF0000";
        Assert.True(transitions.SaveNow());
        Assert.Equal(new TransitionOptions(WallpaperTransitionStyle.Wave, 3, 12, false, "#FF0000", .05, 90), settings.Load().Transition);
        Find<TextBox>(transitions, "TransitionGlowColor").Text = "";
        Assert.True(transitions.SaveNow());

        var export = Environment.GetEnvironmentVariable("BELIE_TOPBAR_EXPORT");
        foreach (var (name, page) in new (string, System.Windows.Controls.UserControl)[] { ("bar", bar), ("workspaces", spaces), ("transitions", transitions) })
        {
            if (name == "bar") bar.Reload(); else if (name == "workspaces") spaces.Reload();
            var window = new Window { Content = page, Width = 860, Height = 760, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            window.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "BgBrush");
            window.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
            window.Show();
            var tabs = Within<TabControl>(page).Single();
            for (var tab = 0; tab < tabs.Items.Count; tab++)
            {
                tabs.SelectedIndex = tab;
                PumpUntil(() => true); window.UpdateLayout();
                if (!string.IsNullOrEmpty(export)) Capture((FrameworkElement)page, Path.Combine(export, $"{name}-{tab}.png"));
            }
            if (page == transitions)
            {
                tabs.SelectedIndex = 0; PumpUntil(() => true);
                var preview = Within<TransitionPreview>(page).Single();
                foreach (var style in Enum.GetValues<WallpaperTransitionStyle>())
                {
                    var asked = false;
                    preview.Options = () => { asked = true; return new TransitionOptions(style, .3, 8); };
                    preview.Play();
                    PumpUntil(() => asked);
                    PumpUntil(() => !preview.IsPlaying);
                }
                preview.Options = () => new TransitionOptions(WallpaperTransitionStyle.Wave, 3, 8);
                preview.Play(); PumpUntil(() => preview.IsPlaying);
                if (!string.IsNullOrEmpty(export))
                {
                    var until = DateTime.UtcNow.AddSeconds(1.5); PumpUntil(() => DateTime.UtcNow > until);
                    Capture(page, Path.Combine(export, "transitions-playing.png"));
                }
            }
            window.Close();
        }
        Assert.False(Within<TransitionPreview>(transitions).Single().IsPlaying);
    }

    private static void Capture(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static IEnumerable<T> Within<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Within<T>(child)) yield return nested;
        }
    }
}
