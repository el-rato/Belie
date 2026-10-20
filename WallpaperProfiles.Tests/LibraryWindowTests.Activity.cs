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
    private static void CheckActivityAndSceneControls(MainWindow main, WallpaperCoordinator coordinator, TestClock clock,
        WallpaperProfile profile, string imagePath)
    {
        profile.SceneAccent = ScenePresets.Create("Focus").SceneAccent;
        coordinator.SwitchManually(profile.Id);
        Click(main, "DashboardNavButton");
        Assert.Equal(Visibility.Visible, ((FrameworkElement)main.FindName("DashboardPane")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)main.FindName("ProfilePane")).Visibility);
        Assert.Equal("Focus", ((TextBlock)main.FindName("ActiveProfileText")).Text);
        Assert.Equal("Manual choice", ((TextBlock)main.FindName("ActivationReasonText")).Text);
        Assert.True(WallpaperProfiles.Engine.SceneController.TryParseAccent(profile.SceneAccent, out var color));
        Assert.Equal(color, (System.Windows.Media.Color)System.Windows.Application.Current.Resources["SceneAccentColor"]);
        main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        Assert.Equal(color, ((SolidColorBrush)main.FindResource("AccentBrush")).Color);
        Click(main, "StartOverrideButton");
        Assert.True(coordinator.GetActivitySnapshot().ManualOverride?.IsTimed);
        Assert.Contains("60 min left", ((TextBlock)main.FindName("OverrideStatusText")).Text);
        Click(main, "PauseAutomationButton");
        Assert.Equal("Paused", ((TextBlock)main.FindName("AutomationModeText")).Text);
        Click(main, "ResumeAutomaticButton");
        Assert.False(coordinator.Paused);
        Assert.Null(coordinator.GetActivitySnapshot().ManualOverride);
        Assert.Equal("Running", ((TextBlock)main.FindName("AutomationModeText")).Text);
        Click(main, "StartOverrideButton");
        clock.Advance(TimeSpan.FromHours(1));
        coordinator.CheckNow();
        Assert.Equal("No manual override is active.", ((TextBlock)main.FindName("OverrideStatusText")).Text);
        Assert.NotEmpty(((ItemsControl)main.FindName("RecentActivityList")).Items);
        RenderWorkspace(main, "activity", 1040, 790);
        RenderWorkspace(main, "activity-compact", 880, 650);
        var dashboard = (ScrollViewer)main.FindName("DashboardPane");
        var settingsButton = (Button)main.FindName("SettingsButton");
        var root = (FrameworkElement)main.Content;
        var settingsPosition = settingsButton.TranslatePoint(new System.Windows.Point(), root);
        Assert.True(settingsButton.IsVisible);
        Assert.InRange(settingsPosition.Y + settingsButton.ActualHeight, 1, root.ActualHeight);
        Assert.InRange(dashboard.ViewportHeight, 1, root.ActualHeight - 47);
        Assert.True(dashboard.ScrollableHeight > 0);
        dashboard.ScrollToTop(); main.UpdateLayout();
        var sceneCard = VisualChildren((StackPanel)main.FindName("SceneGallery")).OfType<Button>().First();
        var galleryWheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
        sceneCard.RaiseEvent(galleryWheel);
        if (!galleryWheel.Handled)
            sceneCard.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(galleryWheel.MouseDevice, galleryWheel.Timestamp, galleryWheel.Delta)
                { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
        main.UpdateLayout();
        Assert.True(dashboard.VerticalOffset > 0, "Wheel scrolling over scene cards must scroll the Activity page.");
        dashboard.ScrollToTop(); main.UpdateLayout();
        var recentActivity = (ItemsControl)main.FindName("RecentActivityList");
        var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent };
        recentActivity.RaiseEvent(wheel); main.UpdateLayout();
        Assert.True(wheel.Handled);
        Assert.True(dashboard.VerticalOffset > 0);
        dashboard.ScrollToEnd(); main.UpdateLayout();
        Assert.Equal(dashboard.ScrollableHeight, dashboard.VerticalOffset, precision: 1);
        var lastActivity = (FrameworkElement)recentActivity.ItemContainerGenerator.ContainerFromIndex(recentActivity.Items.Count - 1);
        var lastPosition = lastActivity.TranslatePoint(new System.Windows.Point(), dashboard);
        Assert.InRange(lastPosition.Y + lastActivity.ActualHeight, 1, dashboard.ActualHeight);
        Click(main, "SettingsButton");
        Assert.Contains(System.Windows.Application.Current.Windows.OfType<Window>(), w => w.Owner == main && w.Title == "Belie settings");
        Click(main, "SettingsButton");
        RenderWorkspace(main, "activity-scrolled", 880, 650);
        dashboard.ScrollToTop(); main.UpdateLayout();

        var editor = new ProfileEditorWindow(new WallpaperProfile { FolderPath = imagePath }, isNew: true)
            { ShowInTaskbar = false, Opacity = 0, ChooseDraftAction = () => DraftChoice.Discard };
        editor.Loaded += (_, _) =>
        {
            ((ComboBox)editor.FindName("PresetCombo")).SelectedItem = "Gaming";
            Assert.Equal("Gaming", ((TextBox)editor.FindName("NameBox")).Text);
            Assert.Equal("#ADBDF4", ((TextBox)editor.FindName("SceneAccentBox")).Text);
            Assert.True(((CheckBox)editor.FindName("AmbientMuteBox")).IsChecked);
            Assert.Equal(15, ((Slider)editor.FindName("AmbientVolumeSlider")).Value);
            ((TextBox)editor.FindName("AudioPathBox")).Text = Path.Combine(Path.GetDirectoryName(imagePath)!, "ambient.wav");
            ((TabControl)editor.FindName("EditorTabs")).SelectedIndex = 3;
            RenderWorkspace(editor, "scene-editor", 680, 720);
            typeof(ProfileEditorWindow).GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, new object[] { editor, new RoutedEventArgs() });
        };
        Assert.True(editor.ShowDialog());
        Assert.Equal("#ADBDF4", editor.Result?.SceneAccent);
        Assert.Equal(15, editor.Result?.AmbientVolume);
        Assert.True(editor.Result?.AmbientMuted);
        Assert.EndsWith("ambient.wav", editor.Result?.AmbientAudioPath);
    }
}
