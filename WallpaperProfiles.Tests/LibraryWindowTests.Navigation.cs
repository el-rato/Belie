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
    private static void CheckWorkspaceNavigation(string dir, LibraryStore libraryStore, string imagePath)
    {
        var otherFolder = Path.Combine(dir, "different-folder");
        Directory.CreateDirectory(otherFolder);
        var secondWallpaper = Path.Combine(otherFolder, "sky.png");
        File.Copy(imagePath, secondWallpaper);
        libraryStore.Save(libraryStore.Load().Append(new WallpaperAsset { Name = "Sky beyond", FilePath = secondWallpaper }));
        var profiles = new ProfileStore(Path.Combine(dir, "profiles"));
        var settings = new SettingsStore(Path.Combine(dir, "settings.json"));
        var profile = new WallpaperProfile { Name = "Focus", SlideshowIntervalMinutes = 5 };
        profiles.Save(profile);
        var clock = new TestClock();
        using var coordinator = new WallpaperCoordinator(profiles, settings, new AppSettings(), clock, _ => { });
        typeof(WallpaperCoordinator).GetField("_profiles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(coordinator, new List<WallpaperProfile> { profile });
        using var shortcuts = new ProfileShortcutManager(() => coordinator.Profiles, profiles.Save,
            coordinator.ProfilesEdited, id => coordinator.SwitchManually(id), new ProfileShortcutTests.Hotkeys());
        var main = new MainWindow(coordinator, profiles, settings, libraryStore, shortcuts) { ChooseDraftAction = () => DraftChoice.Discard };
        // Present real popup controls without invoking startup and registry changes.
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main,
            typeof(MainWindow).GetMethod("OnLoaded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!);
        main.Opacity = 0; main.ShowInTaskbar = false; main.ShowActivated = false; main.Show();
        typeof(MainWindow).GetMethod("BuildRail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
        typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profile.Id });
        var profilePane = (FrameworkElement)main.FindName("ProfilePane");
        var libraryPane = (ContentControl)main.FindName("LibraryPane");
        profile.Schedule.Add(new ScheduleRule { DaysOfWeek = new() { DayOfWeek.Monday, DayOfWeek.Friday },
            StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0) });
        profile.EventTriggers.Add(new WallpaperProfiles.Models.EventTrigger { Type = TriggerType.BelowBatteryPercent, Condition = "15", Priority = 8 });
        typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profile.Id });
        // Schedules and triggers are edited right on the Automation tab.
        var tabs = (TabControl)main.FindName("ProfileTabs");
        var ruleRows = (System.Collections.ObjectModel.ObservableCollection<ScheduleRuleVm>)((ItemsControl)main.FindName("RulesList")).ItemsSource;
        var triggerRows = (System.Collections.ObjectModel.ObservableCollection<TriggerVm>)((ItemsControl)main.FindName("TriggersList")).ItemsSource;
        Assert.Equal("22:00", Assert.Single(ruleRows).StartTimeText);
        Assert.True(ruleRows[0].Days[(int)DayOfWeek.Monday]);
        Assert.Equal("15", Assert.Single(triggerRows).Condition);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)main.FindName("NoRulesHint")).Visibility);
        tabs.SelectedIndex = 2;
        RenderWorkspace(main, "profile-rules", 880, 650);
        Click(main, "AddRuleButton");
        Assert.Equal(2, ruleRows.Count);
        Assert.True(main.IsDraftDirty);
        triggerRows[0].Condition = "";
        tabs.SelectedIndex = 0;
        Click(main, "SaveProfileButton");
        Assert.Equal(2, tabs.SelectedIndex);
        Assert.Contains("positive number", ((TextBlock)main.FindName("ProfileMessage")).Text);
        Assert.True(main.IsDraftDirty);
        triggerRows[0].Condition = "20";
        Click(main, "SaveProfileButton");
        Assert.False(main.IsDraftDirty);
        var savedRules = profiles.LoadAll().Single(p => p.Id == profile.Id);
        Assert.Equal(2, savedRules.Schedule.Count);
        Assert.Equal("20", savedRules.EventTriggers.Single().Condition);
        ruleRows.Clear(); triggerRows.Clear();
        Assert.Equal(Visibility.Visible, ((FrameworkElement)main.FindName("NoRulesHint")).Visibility);
        Click(main, "SaveProfileButton");
        Assert.Empty(profiles.LoadAll().Single(p => p.Id == profile.Id).Schedule);
        profile = coordinator.Profiles.Single(p => p.Id == profile.Id);
        profile.Schedule.Clear(); profile.EventTriggers.Clear();
        typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profile.Id });
        ((TabControl)main.FindName("ProfileTabs")).SelectedIndex = 0;

        Click(main, "LibraryNavButton");
        Assert.Equal(Visibility.Collapsed, profilePane.Visibility);
        Assert.Equal(Visibility.Visible, libraryPane.Visibility);
        var library = (LibraryWindow)typeof(MainWindow).GetField("_libraryView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        ((FrameworkElement)library.FindName("LibraryRoot")).RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var gallery = (ListBox)library.FindName("Gallery");
        Assert.Equal(48, gallery.Items.Count);
        Assert.Equal(main.FindResource("TextPrimaryBrush"), gallery.Foreground);
        Assert.Same(libraryPane.Content, library.FindName("LibraryRoot"));
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)library.FindName("LibraryChrome")).Visibility);

        RenderWorkspace(main, "library", 1040, 790);
        RenderWorkspace(main, "library-compact", 880, 650);
        Click(library, "BackButton");
        Assert.Equal(Visibility.Visible, profilePane.Visibility);
        Assert.Equal(Visibility.Collapsed, libraryPane.Visibility);
        Click(main, "LibraryNavButton");
        Assert.Same(library, typeof(MainWindow).GetField("_libraryView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main));
        Click(main, "ProfilesNavButton");
        Assert.Equal(Visibility.Visible, profilePane.Visibility);
        Click(main, "LibraryNavButton");
        gallery.SelectedIndex = 0;
        Click(library, "UseButton");
        Assert.Equal(Visibility.Visible, profilePane.Visibility);
        Assert.Equal(Visibility.Collapsed, libraryPane.Visibility);
        Assert.Equal(imagePath, ((TextBox)main.FindName("FolderBox")).Text);
        Assert.Equal("Focus", ((TextBox)main.FindName("NameBox")).Text);
        Assert.Equal("", Assert.Single(profiles.LoadAll()).FolderPath);
        Click(main, "AddFromLibraryButton");
        var librarySearch = (TextBox)library.FindName("SearchBox");
        gallery.SelectedIndex = -1;
        main.UpdateLayout();
        var firstPick = VisualChildren(gallery).OfType<CheckBox>().First(box => box.IsEnabled);
        firstPick.IsChecked = true;
        Click(library, "NextButton");
        Assert.Contains("1 selected", ((TextBlock)library.FindName("LibrarySelectionText")).Text);
        Assert.True(((Button)library.FindName("UseButton")).IsEnabled);
        librarySearch.Text = "Sky beyond";
        main.UpdateLayout();
        VisualChildren(gallery).OfType<CheckBox>().Single().IsChecked = true;
        Assert.Equal("Add 2 wallpapers", ((Button)library.FindName("UseButton")).Content);
        Click(library, "UseButton");
        Assert.Equal(imagePath, ((TextBox)main.FindName("FolderBox")).Text);
        var wallpapers = (ItemsControl)main.FindName("ProfileWallpaperList");
        PumpUntil(() => wallpapers.Items.Count == 2);
        Assert.Equal(new[] { imagePath, secondWallpaper }, wallpapers.Items.Cast<string>());
        Click(main, "SaveProfileButton");
        Assert.Equal(new[] { secondWallpaper }, Assert.Single(profiles.LoadAll()).AdditionalWallpaperPaths);
        profile = coordinator.Profiles.Single(p => p.Id == profile.Id);
        main.UpdateLayout();
        RenderWorkspace(main, "profile-wallpaper-list", 1040, 790);
        PumpUntil(() => wallpapers.Items.Count == 2);
        main.UpdateLayout();
        var thumbnails = VisualChildren(wallpapers).OfType<SourceThumbnail>().ToArray();
        Assert.Equal(new[] { imagePath, secondWallpaper }, thumbnails.Select(image => image.FilePath));
        PumpUntil(() => thumbnails.All(image => image.Source != null));
        Assert.DoesNotContain(VisualChildren(wallpapers).OfType<TextBlock>(), text =>
            text.Text.Contains(Path.GetFileName(imagePath)) || text.Text.Contains(Path.GetFileName(secondWallpaper)));
        MoveWallpaper(main, secondWallpaper, "-1");
        PumpUntil(() => wallpapers.Items.Cast<string>().SequenceEqual(new[] { secondWallpaper, imagePath }));
        Assert.True(main.IsDraftDirty);
        Assert.Empty(Assert.Single(profiles.LoadAll()).WallpaperOrder);
        Click(main, "SaveProfileButton");
        Assert.Equal(new[] { secondWallpaper, imagePath }, Assert.Single(profiles.LoadAll()).WallpaperOrder);
        MoveWallpaper(main, secondWallpaper, "1");
        PumpUntil(() => wallpapers.Items.Cast<string>().SequenceEqual(new[] { imagePath, secondWallpaper }));
        Click(main, "SaveProfileButton");
        Assert.True(File.Exists(secondWallpaper));
        Click(main, "AddFromLibraryButton");
        librarySearch.Text = "Sky beyond";
        gallery.SelectedIndex = 0;
        Click(library, "UseButton");
        Click(main, "SaveProfileButton");
        Assert.Equal(new[] { secondWallpaper }, Assert.Single(profiles.LoadAll()).AdditionalWallpaperPaths);
        profile = coordinator.Profiles.Single(p => p.Id == profile.Id);
        var profileTabs = (TabControl)main.FindName("ProfileTabs");
        Assert.Equal(3, profileTabs.Items.Count);
        profileTabs.SelectedIndex = 1;
        RenderWorkspace(main, "profiles-playback", 1040, 790);
        Assert.Equal("Focus", ((TextBox)main.FindName("NameBox")).Text);
        Assert.Equal(imagePath, ((TextBox)main.FindName("FolderBox")).Text);
        profileTabs.SelectedIndex = 0;
        Click(main, "SettingsButton");
        Assert.Contains(System.Windows.Application.Current.Windows.OfType<Window>(), w => w.Owner == main && w.Title == "Belie settings");
        CheckPatchNotes(main);
        Click(main, "SettingsButton");
        Assert.DoesNotContain(System.Windows.Application.Current.Windows.OfType<Window>(), w => w.Owner == main && w.Title == "Belie settings");
        CheckAppearance(main, coordinator, settings, profile);
        CheckProfileDrafts(main, profiles, coordinator, profile.Id, imagePath);
        CheckThumbnailDispatch(main);
        profile = coordinator.Profiles.Single(p => p.Id == profile.Id);
        RenderWorkspace(main, "profiles", 1040, 790);
        RenderWorkspace(main, "profiles-compact", 880, 650);
        CheckActivityAndSceneControls(main, coordinator, clock, profile, imagePath);
        CheckDesktopCanvasPanel(main, dir, imagePath);
        CheckWidgetDrafts(main);
        CaptureAuditViews(main, profile.Id);
        typeof(MainWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { profile.Id });
        Click(main, "ShortcutButton");
        var shortcutPopup = (System.Windows.Controls.Primitives.Popup)main.FindName("ShortcutPopup");
        Assert.True(shortcutPopup.IsOpen);
        Assert.True(shortcuts.IsCapturing);
        typeof(MainWindow).GetField("_pendingShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, "Ctrl+Alt+1");
        typeof(MainWindow).GetMethod("AssignShortcut_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
        Assert.False(shortcutPopup.IsOpen);
        Assert.False(shortcuts.IsCapturing);
        Assert.Equal("Ctrl+Alt+1", profiles.LoadAll().Single(p => p.Id == profile.Id).KeyboardShortcut);
        Assert.Equal("Ctrl + Alt + 1", ((Button)main.FindName("ShortcutButton")).Content);
        UiAppearance.Apply("Ember");
        RenderWorkspace(main, "profiles-shortcuts-ember", 1040, 790);
        RenderWorkspace(main, "profiles-shortcuts-compact", 880, 650);
        Click(main, "ShortcutButton");
        typeof(MainWindow).GetMethod("RemoveShortcut_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { main, new RoutedEventArgs() });
        Assert.Empty(profiles.LoadAll().Single(p => p.Id == profile.Id).KeyboardShortcut);
        Assert.Equal("Assign shortcut", ((Button)main.FindName("ShortcutButton")).Content);
        CheckNowPlayingWidget(main, dir);
        CheckShellPages(main, settings, dir);
        library.Close();
        CheckUnload(() => new MainWindow(coordinator, profiles, settings, libraryStore, shortcuts) { ChooseDraftAction = () => DraftChoice.Discard });
    }

    private static void CheckShellPages(MainWindow main, SettingsStore settings, string dir)
    {
        typeof(MainWindow).GetProperty("TopBarFile", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, Path.Combine(dir, "shell-topbar.json"));
        typeof(MainWindow).GetProperty("StartTopBar", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, new Action<string>(_ => { }));
        foreach (var name in new[] { "TopBar", "Workspaces", "Transitions", "Switcher" })
        {
            Click(main, name + "NavButton");
            var pane = (ContentControl)main.FindName(name + "Pane");
            Assert.Equal(Visibility.Visible, pane.Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)main.FindName("ProfilePane")).Visibility);
            Assert.Equal("Selected", ((Button)main.FindName(name + "NavButton")).Tag);
            RenderWorkspace(main, "shell-" + name.ToLowerInvariant(), 1180, 820);
            RenderWorkspace(main, "shell-" + name.ToLowerInvariant() + "-compact", 700, 650);
        }
        var page = (TransitionsPage)((ContentControl)main.FindName("TransitionsPane")).Content;
        VisualChildren(page).OfType<Button>().Single(button => Equals(button.Content, "Pixel colonies")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(page.SaveNow());
        Assert.Equal(WallpaperTransitionStyle.Colonies, settings.Load().WallpaperTransition);

        // The top bar never lets the page pills slide under the status pills, at any width.
        var navBar = (FrameworkElement)main.FindName("NavBar");
        var statusPills = (FrameworkElement)main.FindName("StatusPills");
        var topBar = (FrameworkElement)main.FindName("TopBar");
        foreach (var width in new[] { 1600, 1460, 1180, 1000, 860, 700 })
        {
            main.Width = width; main.UpdateLayout();
            typeof(MainWindow).GetMethod("UpdateShellLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            main.UpdateLayout();
            var navRight = navBar.TranslatePoint(new System.Windows.Point(navBar.ActualWidth, 0), topBar).X;
            var statusLeft = statusPills.TranslatePoint(new System.Windows.Point(), topBar).X;
            Assert.True(navRight <= statusLeft, $"At {width}px the page pills end at {navRight:0} but the status pills start at {statusLeft:0}.");
        }
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)main.FindName("BrandName")).Visibility);
        main.Width = 1600; main.UpdateLayout();
        typeof(MainWindow).GetMethod("UpdateShellLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
        // Windows keeps a window within the screen, so on a small screen (as on build machines) it never gets this wide.
        if (main.ActualWidth >= 1500) Assert.Equal(Visibility.Visible, ((FrameworkElement)main.FindName("BrandName")).Visibility);
        RenderWorkspace(main, "shell-topbar-1460", 1460, 820);
        RenderWorkspace(main, "shell-topbar-1100", 1100, 820);
        main.Width = 1180; main.UpdateLayout();

        // The launcher: open it, type, and the best match runs on Enter.
        main.OpenLauncher();
        var box = (TextBox)main.FindName("LauncherBox");
        var results = (ListBox)main.FindName("LauncherList");
        Assert.True(main.LauncherOpen);
        Assert.NotEmpty(results.Items);
        RenderWorkspace(main, "shell-launcher", 1180, 820);
        box.Text = "lib";
        Assert.Equal("Library", ((LauncherCommand)results.SelectedItem).Title);
        RenderWorkspace(main, "shell-launcher-typed", 1180, 820);
        box.Text = "zzzz nothing";
        Assert.Empty(results.Items);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)main.FindName("LauncherEmpty")).Visibility);
        box.Text = "lib";
        typeof(MainWindow).GetMethod("RunLauncherCommand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new[] { results.SelectedItem });
        Assert.False(main.LauncherOpen);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)main.FindName("LibraryPane")).Visibility);
        Click(main, "SearchPill");
        Assert.True(main.LauncherOpen);
        main.CloseLauncher();
        Assert.False(main.LauncherOpen);
        Click(main, "ProfilesNavButton");
        RenderWorkspace(main, "shell-profiles", 1180, 820);
        RenderWorkspace(main, "shell-profiles-compact", 700, 650);
    }

    private static void CheckUnload(Func<MainWindow> create)
    {
        var window = OpenAndUnload(create);
        // Work the window started may hold it for a moment after it closes.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (window.IsAlive && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
        Assert.False(window.IsAlive, "Something still holds the closed main window, so its memory would never be freed.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference OpenAndUnload(Func<MainWindow> create)
    {
        var main = create();
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main,
            typeof(MainWindow).GetMethod("OnLoaded", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!);
        main.Opacity = 0; main.ShowInTaskbar = false; main.ShowActivated = false; main.Show();
        Assert.False(main.TryUnload()); // Still on screen.
        Click(main, "LibraryNavButton");
        Click(main, "CanvasNavButton");
        main.Close();
        Assert.False(main.IsVisible);
        Assert.True(main.TryUnload());
        Assert.DoesNotContain(main, System.Windows.Application.Current.Windows.OfType<MainWindow>());
        return new WeakReference(main);
    }
}
