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
        CheckEditorLibrary(libraryStore, imagePath, secondWallpaper);
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
        Assert.Contains("22:00 – 06:00 · overnight", ((TextBlock)main.FindName("ScheduleSummary")).Text);
        Assert.Contains(CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Monday), ((TextBlock)main.FindName("ScheduleSummary")).Text);
        Assert.Equal("battery ≤ 15% · priority 8", ((TextBlock)main.FindName("TriggerSummary")).Text);
        Assert.Equal("Edit schedules & triggers", ((Button)main.FindName("EditRulesButton")).Content);
        ((TabControl)main.FindName("ProfileTabs")).SelectedIndex = 2;
        RenderWorkspace(main, "profile-rules", 880, 650);
        foreach (var (button, tab) in new[] { ("EditRulesButton", 2) })
        {
            Exception? editorFailure = null;
            main.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                var editor = System.Windows.Application.Current.Windows.OfType<ProfileEditorWindow>().Single(window => window.Owner == main);
                try
                {
                    Assert.Equal(tab, ((TabControl)editor.FindName("EditorTabs")).SelectedIndex);
                    Assert.Single(((ItemsControl)editor.FindName("RulesList")).Items);
                }
                catch (Exception ex) { editorFailure = ex; }
                finally { editor.Close(); }
            }));
            Click(main, button);
            if (editorFailure != null) throw editorFailure;
        }
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
        Assert.Equal(4, profileTabs.Items.Count);
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
        library.Close();
    }
}
