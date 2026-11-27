using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
    private Guid? _toastedActive;

    private void CoordinatorStateChanged()
    {
        // A short note whenever a different profile reaches the desktop, however it got there.
        var active = _coordinator.ActiveProfile;
        if (active != null && _toastedActive != null && active.Id != _toastedActive) ShowToast(active.Name + " is on your desktop");
        _toastedActive = active?.Id ?? _toastedActive;
        RefreshActiveGlow();
        RefreshDashboard();
        if (CanvasPane.Content is DesktopCanvasView canvas) canvas.RefreshProfiles();
    }

    private string? _heroKey;

    /// <summary>
    /// The Overview card shows the wallpaper that is on the desktop at full size. The small profile thumbnail
    /// stands in while the large one loads, and it also feeds the soft glow around the card.
    /// </summary>
    private void RefreshHero(WallpaperProfile? active)
    {
        var current = _coordinator.CurrentMediaPath;
        var key = active == null ? null : active.Id + "|" + current;
        var monitor = _railEntries.FirstOrDefault(entry => entry.Id == active?.Id)?.Monitor;
        if (key == _heroKey && ReferenceEquals(monitor, _activityPreviewMonitor)) return;
        _activityPreviewMonitor = monitor;
        _heroKey = key;
        HeroGlowBrush.ImageSource = null;
        BindingOperations.ClearBinding(ActivityWallpaperBrush, ImageBrush.ImageSourceProperty);
        if (monitor != null)
            BindingOperations.SetBinding(ActivityWallpaperBrush, ImageBrush.ImageSourceProperty,
                new System.Windows.Data.Binding(nameof(MonitorThumbnail.ScreenSource)) { Source = monitor });
        else ActivityWallpaperBrush.ImageSource = null;
        if (active == null) return;
        var profile = active;
        _ = LoadHeroAsync(key!, profile, current);
    }

    private async Task LoadHeroAsync(string key, WallpaperProfile profile, string current)
    {
        (ImageSource? Image, ImageSource? Glow) loaded;
        try
        {
            loaded = await Task.Run(() =>
            {
                var path = !string.IsNullOrEmpty(current) && File.Exists(current) ? current : Engine.WallpaperEngine.GetProfileMedia(profile).FirstOrDefault();
                // The glow is the same picture only a few dozen pixels wide; stretched up, it is already soft.
                return (ThumbnailLoader.Load(path, 1920), ThumbnailLoader.Load(path, 24));
            });
        }
        catch (Exception ex) { Infrastructure.Logger.Error("Loading the Overview wallpaper failed.", ex); return; }
        if (key != _heroKey || Dispatcher.HasShutdownStarted) return;
        HeroGlowBrush.ImageSource = loaded.Glow;
        if (loaded.Image == null) return;
        BindingOperations.ClearBinding(ActivityWallpaperBrush, ImageBrush.ImageSourceProperty);
        ActivityWallpaperBrush.ImageSource = loaded.Image;
    }

    private void Hero_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Tall enough to feel like the desktop itself, never so tall that the rest of the page disappears.
        var height = Math.Clamp(e.NewSize.Width * 0.4, 300, 520);
        if (Math.Abs(HeroCard.Height - height) > 1) HeroCard.Height = height;
    }

    private void RefreshDashboard()
    {
        var snapshot = _coordinator.GetActivitySnapshot();
        var now = _coordinator.LocalNow;
        ActiveProfileText.Text = snapshot.ActiveProfile?.Name ?? "No profile applied";
        ActiveStateText.Text = snapshot.ActiveProfile == null ? "NO PROFILE APPLIED" : "APPLIED PROFILE";
        ActiveStateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, snapshot.ActiveProfile == null ? "TextTertiaryBrush" : "GoodBrush");
        EditActiveProfileButton.IsEnabled = snapshot.ActiveProfile != null;
        RefreshHero(snapshot.ActiveProfile);
        UpdateShellLayout();
        ActivationReasonText.Text = snapshot.Reason;
        AutomationModeText.Text = snapshot.Paused ? "Paused" : "Running";
        AutomationHintText.Text = snapshot.Paused ? "Manual choices available" : "Schedules & triggers enabled";
        PauseAutomationButton.Content = snapshot.Paused ? "Resume" : "Pause";
        ActivePillDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, snapshot.ActiveProfile == null ? "TextTertiaryBrush" : "GoodBrush");
        AutomationPillIcon.Text = snapshot.Paused ? "\uE769" : snapshot.ManualOverride != null ? "\uE916" : "\uE945";
        AutomationPillText.Text = snapshot.Paused ? "paused"
            : snapshot.ManualOverride is { IsTimed: true, EndsAtLocal: { } ends } ? $"override {Math.Max(0, (int)Math.Ceiling((ends - now).TotalMinutes))}m"
            : snapshot.ManualOverride != null ? "override" : "auto";
        AutomationPill.ToolTip = snapshot.Paused ? "Automation is paused. Click to resume schedules and triggers."
            : "Schedules and triggers are on. Click to pause them.";
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
}
