using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Net.Http;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed partial class LibraryWindowTests
{
    private static void CheckThumbnailDispatch(MainWindow main)
    {
        var rail = (StackPanel)main.FindName("RailStack");
        var frame = new System.Windows.Threading.DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        bool Loaded() => VisualChildren(rail).OfType<MonitorThumbnail>().Any(m => m.ScreenSource != null)
            && VisualChildren(main).OfType<SourceThumbnail>().Any(t => t.Source != null);
        timer.Tick += (_, _) => { if (Loaded() || DateTime.UtcNow > deadline) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        Assert.True(Loaded(), "Profile and source thumbnails must load on their UI dispatcher.");
    }

    private static void CheckProfileDrafts(MainWindow main, ProfileStore store, WallpaperCoordinator coordinator, Guid id, string imagePath)
    {
        var other = new WallpaperProfile { Name = "Other profile", FolderPath = imagePath };
        store.Save(other); coordinator.ProfilesEdited();
        void Select(Guid profileId) => typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profileId });
        var name = (TextBox)main.FindName("NameBox");
        var interval = (TextBox)main.FindName("IntervalBox");
        var originalName = name.Text;
        name.Text = "Reverted draft";
        name.Text = originalName;
        Assert.False(main.IsDraftDirty);
        Assert.NotEqual("Unsaved changes", ((TextBlock)main.FindName("ProfileMessage")).Text);
        name.Text = "Pending name";
        Assert.True(main.IsDraftDirty);
        Assert.Equal("Focus", coordinator.Profiles.Single(p => p.Id == id).Name);
        main.ChooseDraftAction = () => DraftChoice.Cancel;
        Select(other.Id);
        Assert.Equal("Pending name", name.Text);
        Assert.True(main.IsDraftDirty);
        main.ChooseDraftAction = () => DraftChoice.Save;
        interval.Text = "invalid";
        Select(other.Id);
        Assert.Equal("Pending name", name.Text);
        Assert.Equal(1, ((TabControl)main.FindName("ProfileTabs")).SelectedIndex);
        Assert.Contains("whole number", ((TextBlock)main.FindName("ProfileMessage")).Text);
        interval.Text = "5";
        Select(other.Id);
        Assert.Equal("Pending name", store.LoadAll().Single(p => p.Id == id).Name);
        Assert.Equal("Other profile", name.Text);
        Select(id);
        name.Text = "Discard this name";
        main.ChooseDraftAction = () => DraftChoice.Discard;
        Select(other.Id);
        Assert.Equal("Pending name", store.LoadAll().Single(p => p.Id == id).Name);
        Select(id); name.Text = "Focus"; Click(main, "SaveProfileButton");
        Assert.False(main.IsDraftDirty);
        Assert.Equal("Changes saved", ((TextBlock)main.FindName("ProfileMessage")).Text);
        var discardCalled = false;
        Assert.False(DraftGuard.Confirm(main, "fixture", () => false, () => discardCalled = true, () => DraftChoice.Save));
        Assert.False(discardCalled);
        store.Delete(other.Id); coordinator.ProfilesEdited();
        typeof(MainWindow).GetMethod("BuildRail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
        Select(id);
        ((TabControl)main.FindName("ProfileTabs")).SelectedIndex = 0;
        var commands = (Button)main.FindName("ApplyProfileButton");
        RenderWorkspace(main, "profile-pinned-commands", 880, 650);
        ((ScrollViewer)main.FindName("ProfileDetailsScroll")).ScrollToEnd(); main.UpdateLayout();
        var commandPosition = commands.TranslatePoint(new System.Windows.Point(), (UIElement)main.Content);
        Assert.InRange(commandPosition.Y + commands.ActualHeight, 1, main.ActualHeight);
        ((ScrollViewer)main.FindName("ProfileDetailsScroll")).ScrollToTop();
    }

    private static void CheckEditorLibrary(LibraryStore store, string imagePath, string secondWallpaper)
    {
        Exception? failure = null;
        var model = new WallpaperProfile { Name = "Picker check", SlideshowIntervalMinutes = 0 };
        var editor = new ProfileEditorWindow(model, isNew: true, libraryStore: store)
            { ShowInTaskbar = false, Opacity = 0 };
        editor.Loaded += (_, _) =>
        {
            editor.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(async () =>
            {
                var picker = System.Windows.Application.Current.Windows.OfType<LibraryWindow>().Single(w => w.Owner == editor);
                try
                {
                    await WaitUntil(() => ((Button)picker.FindName("ImportFilesButton")).IsEnabled);
                    var gallery = (ListBox)picker.FindName("Gallery");
                    var search = (TextBox)picker.FindName("SearchBox");
                    foreach (var query in new[] { "Forest retreat", "Sky beyond" })
                    {
                        search.Text = query;
                        picker.UpdateLayout();
                        VisualChildren(gallery).OfType<CheckBox>().Single().IsChecked = true;
                    }
                    Assert.Equal("Add 2 wallpapers", ((Button)picker.FindName("UseButton")).Content);
                    Assert.Contains("Picker check", ((TextBlock)picker.FindName("LibrarySelectionText")).Text);
                    Click(picker, "UseButton");
                }
                catch (Exception ex) { failure = ex; picker.Close(); }
            }));
            try
            {
                Click(editor, "AddFromLibraryButton");
                if (failure != null) throw failure;
                Assert.Equal(imagePath, ((TextBox)editor.FindName("FolderBox")).Text);
                Assert.Equal("5", ((TextBox)editor.FindName("IntervalBox")).Text);
                var wallpapers = (ItemsControl)editor.FindName("ProfileWallpaperList");
                PumpUntil(() => wallpapers.Items.Count == 2);
                Assert.Equal(new[] { imagePath, secondWallpaper }, wallpapers.Items.Cast<string>());
                Assert.Empty(model.AdditionalWallpaperPaths);
                Assert.Equal("", model.FolderPath);
                RenderWorkspace(editor, "profile-editor-library", 680, 720);
                MoveWallpaper(editor, secondWallpaper, "-1");
                PumpUntil(() => wallpapers.Items.Cast<string>().SequenceEqual(new[] { secondWallpaper, imagePath }));
                Assert.Empty(model.WallpaperOrder);
                typeof(ProfileEditorWindow).GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(editor, new object[] { editor, new RoutedEventArgs() });
            }
            catch (Exception ex) { failure = ex; editor.ChooseDraftAction = () => DraftChoice.Discard; editor.Close(); }
        };
        var saved = editor.ShowDialog();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.True(saved);
        Assert.Empty(model.AdditionalWallpaperPaths);
        Assert.Empty(model.FolderPath);
        model = editor.Result!;
        Assert.Equal(new[] { secondWallpaper }, model.AdditionalWallpaperPaths);
        Assert.Equal(imagePath, model.FolderPath);
        Assert.Equal(new[] { secondWallpaper, imagePath }, model.WallpaperOrder);

        var cancelEditor = new ProfileEditorWindow(model, isNew: false, libraryStore: store)
            { ShowInTaskbar = false, Opacity = 0, ChooseDraftAction = () => DraftChoice.Discard };
        cancelEditor.Loaded += (_, _) =>
        {
            try
            {
                cancelEditor.UpdateLayout();
                var list = (ItemsControl)cancelEditor.FindName("ProfileWallpaperList");
                PumpUntil(() => list.Items.Count == 2);
                Assert.Equal(new[] { secondWallpaper, imagePath }, list.Items.Cast<string>());
                MoveWallpaper(cancelEditor, secondWallpaper, "1");
                PumpUntil(() => list.Items.Cast<string>().SequenceEqual(new[] { imagePath, secondWallpaper }));
            }
            catch (Exception ex) { failure = ex; }
            finally { cancelEditor.Close(); }
        };
        Assert.False(cancelEditor.ShowDialog());
        if (failure != null) throw failure;
        Assert.Equal(new[] { secondWallpaper }, model.AdditionalWallpaperPaths);
        Assert.Equal(new[] { secondWallpaper, imagePath }, model.WallpaperOrder);
        Assert.True(File.Exists(secondWallpaper));
    }

    private static void CheckTrayMenuTheme(MainWindow main, UiTheme theme)
    {
        using var menu = TrayIconManager.CreateThemedMenu();
        var active = new System.Windows.Forms.ToolStripMenuItem("Chill") { Checked = true };
        menu.Items.Add(active);
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Dark"));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Open Belie"));
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Pause automation"));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Exit"));
        var background = ((SolidColorBrush)main.FindResource("SurfaceBrush")).Color;
        var foreground = ((SolidColorBrush)main.FindResource("TextPrimaryBrush")).Color;
        Assert.Equal(System.Drawing.Color.FromArgb(background.A, background.R, background.G, background.B), menu.BackColor);
        Assert.Equal(System.Drawing.Color.FromArgb(foreground.A, foreground.R, foreground.G, foreground.B), menu.ForeColor);
        menu.PerformLayout();
        Assert.True(active.Checked);
        Assert.All(menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>(), item => Assert.True(item.Height >= 36));
        var path = Environment.GetEnvironmentVariable("BELIE_WORKSPACE_PREVIEW");
        if (!string.IsNullOrEmpty(path))
        {
            using var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(Path.Combine(path, "tray-" + theme.Name.ToLowerInvariant() + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    private static void CheckAppearance(MainWindow main, WallpaperCoordinator coordinator, SettingsStore settings, WallpaperProfile profile)
    {
        var picker = (ListBox)main.FindName("ThemePicker");
        Assert.Equal(UiAppearance.Themes.Count, picker.Items.Count);
        Assert.Equal("Sage", UiAppearance.Find("unknown-theme").Name);
        var icons = new HashSet<string>();
        foreach (var theme in UiAppearance.Themes)
        {
            picker.SelectedItem = theme;
            main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            Assert.Equal(theme.Name, UiAppearance.Current.Name);
            Assert.Equal(theme.Name, settings.Load().UiTheme);
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Background), ((SolidColorBrush)main.Background).Color);
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.Text), ((SolidColorBrush)main.Foreground).Color);
            foreach (var key in new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "AccentTextBrush" })
            {
                var ink = AccentInkConverter.Luminance(((SolidColorBrush)main.FindResource(key)).Color);
                foreach (var background in new[] { "BgBrush", "SurfaceBrush", "SurfaceRaisedBrush" })
                {
                    var paper = AccentInkConverter.Luminance(((SolidColorBrush)main.FindResource(background)).Color);
                    Assert.True((Math.Max(ink, paper) + .05) / (Math.Min(ink, paper) + .05) >= 4.5,
                        key + " contrast in " + theme.Name);
                }
            }
            var icon = (BitmapSource)main.Icon;
            var pixels = new byte[icon.PixelWidth * icon.PixelHeight * 4]; icon.CopyPixels(pixels, icon.PixelWidth * 4, 0);
            icons.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
            using var trayIcon = UiAppearance.CreateTrayIcon();
            Assert.Equal(32, trayIcon.Width);
            CheckTrayMenuTheme(main, theme);
            RenderWorkspace(main, "theme-" + theme.Name.ToLowerInvariant(), 1040, 790);
        }
        Assert.Equal(UiAppearance.Themes.Count, icons.Count);
        picker.SelectedItem = UiAppearance.Find("Ember");
        coordinator.SwitchManually(profile.Id);
        Assert.Equal("Ember", settings.Load().UiTheme);
        Assert.Equal(profile.Id.ToString(), settings.Load().LastActiveProfileId);
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(main);
        Assert.NotNull(chrome);
        Assert.Equal(new Thickness(0), chrome.GlassFrameThickness);
        if (Environment.OSVersion.Version.Build >= 22000) Assert.True(UiAppearance.UpdateFrame(main));
        Click(main, "MaximizeButton");
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(WindowState.Maximized, main.WindowState);
        Assert.Equal("Restore", ((Button)main.FindName("MaximizeButton")).ToolTip);
        var handle = new System.Windows.Interop.WindowInteropHelper(main).Handle;
        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        Assert.True(WallpaperProfiles.Infrastructure.NativeMethods.GetWindowRect(handle, out var bounds));
        Assert.Equal(work.Left, bounds.Left);
        Assert.Equal(work.Top, bounds.Top);
        Assert.Equal(work.Right, bounds.Right);
        Assert.Equal(work.Bottom, bounds.Bottom);
        Click(main, "MaximizeButton");
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(WindowState.Normal, main.WindowState);
        Assert.Equal("Maximize", ((Button)main.FindName("MaximizeButton")).ToolTip);
        profile.SceneAccent = "#B7CDBC";
        coordinator.SwitchManually(profile.Id);
        picker.SelectedItem = UiAppearance.Find("Midnight");
        Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B7CDBC"), System.Windows.Application.Current.Resources["SceneAccentColor"]);
        profile.SceneAccent = "";
        coordinator.SwitchManually(profile.Id);
        Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(UiAppearance.Current.Accent), System.Windows.Application.Current.Resources["SceneAccentColor"]);
        picker.SelectedItem = UiAppearance.Find("Sage");
    }
}
