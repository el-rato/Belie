using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
    private void CoordinatorStateChanged()
    {
        RefreshActiveGlow();
        RefreshDashboard();
        if (CanvasPane.Content is DesktopCanvasView canvas) canvas.RefreshProfiles();
    }

    private void RefreshDashboard()
    {
        var snapshot = _coordinator.GetActivitySnapshot();
        var now = _coordinator.LocalNow;
        ActiveProfileText.Text = snapshot.ActiveProfile?.Name ?? "No profile applied";
        ActiveStateText.Text = snapshot.ActiveProfile == null ? "NO PROFILE APPLIED" : "APPLIED PROFILE";
        ActiveStateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, snapshot.ActiveProfile == null ? "TextTertiaryBrush" : "GoodBrush");
        EditActiveProfileButton.IsEnabled = snapshot.ActiveProfile != null;
        var previewMonitor = _railEntries.FirstOrDefault(entry => entry.Id == snapshot.ActiveProfile?.Id)?.Monitor;
        if (!ReferenceEquals(previewMonitor, _activityPreviewMonitor))
        {
            _activityPreviewMonitor = previewMonitor;
            BindingOperations.ClearBinding(ActivityWallpaperBrush, ImageBrush.ImageSourceProperty);
            if (previewMonitor != null)
                BindingOperations.SetBinding(ActivityWallpaperBrush, ImageBrush.ImageSourceProperty,
                    new System.Windows.Data.Binding(nameof(MonitorThumbnail.ScreenSource)) { Source = previewMonitor });
            else ActivityWallpaperBrush.ImageSource = null;
        }
        ActivationReasonText.Text = snapshot.Reason;
        AmbientStatusText.Text = _coordinator.AmbientStatus;
        var hasAmbient = !string.IsNullOrWhiteSpace(snapshot.ActiveProfile?.AmbientAudioPath);
        AmbientStatusText.Visibility = AmbientMuteButton.Visibility = hasAmbient ? Visibility.Visible : Visibility.Collapsed;
        AmbientMuteButton.IsEnabled = _coordinator.HasAmbientAudio;
        AmbientMuteButton.Visibility = _coordinator.HasAmbientAudio ? Visibility.Visible : Visibility.Collapsed;
        AmbientMuteButton.Content = _coordinator.AmbientIsMuted ? "Unmute ambient" : "Mute ambient";
        AutomationModeText.Text = snapshot.Paused ? "Paused" : "Running";
        AutomationHintText.Text = snapshot.Paused ? "Manual choices available" : "Schedules & triggers enabled";
        PauseAutomationButton.Content = snapshot.Paused ? "Resume" : "Pause";
        NextScheduleText.Text = snapshot.NextScheduleAtLocal is { } next ? FormatWhen(next, now) : "No schedules";
        NextScheduleHint.Text = snapshot.NextScheduleAtLocal == null ? "Add one in Profiles → Automation"
            : snapshot.NextScheduledProfile ?? "Schedule boundary";
        NextScheduleHint.ToolTip = "Overrides and events can take priority.";

        if (!ReferenceEquals(OverrideProfileCombo.ItemsSource, _coordinator.Profiles))
        {
            var id = OverrideProfileCombo.SelectedValue as Guid? ?? _coordinator.ActiveProfileId ?? _selectedId;
            OverrideProfileCombo.ItemsSource = _coordinator.Profiles;
            OverrideProfileCombo.SelectedItem = _coordinator.Profiles.FirstOrDefault(p => p.Id == id) ?? _coordinator.Profiles.FirstOrDefault();
        }
        OverrideProfileCombo.IsEnabled = _coordinator.Profiles.Count > 0;
        StartOverrideButton.IsEnabled = OverrideProfileCombo.SelectedItem is WallpaperProfile;
        ResumeAutomaticButton.IsEnabled = snapshot.Paused || snapshot.ManualOverride != null;
        ResumeAutomaticButton.Visibility = ResumeAutomaticButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        OverrideStatusText.Visibility = snapshot.ManualOverride != null ? Visibility.Visible : Visibility.Collapsed;
        if (snapshot.ManualOverride is { } manual)
        {
            var name = _coordinator.Profiles.FirstOrDefault(p => p.Id == manual.ProfileId)?.Name ?? "Profile";
            var endText = manual.EndsAtLocal is { } end ? "until " + FormatWhen(end, now) : "until you return to automatic rules";
            var remaining = manual.IsTimed && manual.EndsAtLocal.HasValue
                ? $" · {Math.Max(0, (int)Math.Ceiling((manual.EndsAtLocal.Value - now).TotalMinutes))} min left" : "";
            OverrideStatusText.Text = name + " · " + endText + remaining
                + (snapshot.Paused ? " · automation is paused" : "");
        }
        else OverrideStatusText.Text = "No manual override is active.";
        var recent = RecentActivityList.ItemsSource as IReadOnlyList<WallpaperCoordinator.ActivityEntry>;
        if (recent == null || recent.Count != snapshot.Recent.Count || recent.FirstOrDefault() != snapshot.Recent.FirstOrDefault())
            RecentActivityList.ItemsSource = snapshot.Recent;
        NoActivityText.Visibility = snapshot.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatWhen(DateTime time, DateTime now)
        => (time.Date == now.Date ? "Today" : time.Date == now.Date.AddDays(1) ? "Tomorrow" : time.ToString("ddd, d MMM")) + " at " + time.ToString("t");

    private void OverrideProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StartOverrideButton != null) StartOverrideButton.IsEnabled = OverrideProfileCombo.SelectedItem is WallpaperProfile;
    }

    private void StartOverride_Click(object sender, RoutedEventArgs e)
    {
        if (OverrideProfileCombo.SelectedItem is WallpaperProfile profile && OverrideDurationCombo.SelectedItem is OverrideDuration duration)
            _coordinator.SwitchManually(profile.Id, duration.Duration);
    }

    private void PauseAutomation_Click(object sender, RoutedEventArgs e) => _coordinator.TogglePaused();
    private void ResumeAutomatic_Click(object sender, RoutedEventArgs e) => _coordinator.ResumeAutomatic();
    private void AmbientMute_Click(object sender, RoutedEventArgs e) => _coordinator.ToggleAmbientMuted();

    private void CreateScene_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string name }) return;
        var preset = ScenePresets.Create(name, _selected?.FolderPath ?? "");
        for (var suffix = 2; _coordinator.Profiles.Any(p => p.Name.Equals(preset.Name, StringComparison.CurrentCultureIgnoreCase)); suffix++)
            preset.Name = name + " " + suffix;
        AddProfile(template: preset);
    }
}
