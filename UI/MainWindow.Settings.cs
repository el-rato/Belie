using System.Windows;
using System.Windows.Controls;
using WallpaperProfiles.Autostart;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
    private void RefreshUpdateStatus()
    {
        if (System.Windows.Application.Current is not App app) return;
        var updates = app.Updates;
        UpdateStatusText.Text = updates.Status;
        UpdateNoticeText.Text = updates.Status;
        UpdateNotice.Visibility = SettingsUpdateButton.Visibility = AvailableReleaseNotesButton.Visibility = updates.Available != null ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesButton.IsEnabled = UpdateNowButton.IsEnabled = SettingsUpdateButton.IsEnabled = !updates.Busy;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app) await app.Updates.CheckAsync();
    }

    private void ReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        var version = AppUpdater.CurrentVersion.ToString(3);
        var notes = ReleaseNotesWindow.BundledNotes;
        var available = (sender as FrameworkElement)?.Tag as string == "Update"
            ? (System.Windows.Application.Current as App)?.Updates.Available : null;
        if (available != null)
        {
            version = available.TargetFullRelease.Version.ToString();
            notes = ReleaseNotesWindow.UpdateNotes(version, available.TargetFullRelease.NotesMarkdown);
        }
        var dialog = new ReleaseNotesWindow(version, notes, available != null)
        {
            Owner = (sender as DependencyObject) is { } source ? Window.GetWindow(source) ?? this : this
        };
        dialog.ShowDialog();
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app) await app.InstallUpdateAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsDialog != null) { _settingsDialog.Close(); return; }
        var content = SettingsPopup.Child;
        SettingsPopup.Child = null;
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new Window { Title = "Belie settings", Owner = this, Width = 460,
            SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(500, ActualHeight - 60),
            FontSize = 14,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Icon = Icon };
        UiAppearance.SetDialogContent(dialog, scroll, "Settings");
        dialog.Closed += (_, _) => { scroll.Content = null; SettingsPopup.Child = content; _settingsDialog = null; };
        _settingsDialog = dialog; dialog.Show();
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTheme || ThemePicker.SelectedItem is not UiTheme theme) return;
        var previous = _settings.UiTheme;
        try
        {
            _settings.UiTheme = theme.Name;
            _settingsStore.Save(_settings);
            UiAppearance.Apply(theme.Name);
        }
        catch (Exception ex)
        {
            _settings.UiTheme = previous;
            _loadingTheme = true; ThemePicker.SelectedItem = UiAppearance.Find(previous); _loadingTheme = false;
            Logger.Error("Saving appearance failed.", ex);
            System.Windows.MessageBox.Show(this, "The theme could not be saved: " + ex.Message, "Appearance", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }
        AutostartManager.SetEnabled(AutostartToggle.IsChecked == true);
        AutostartToggle.IsChecked = AutostartManager.IsEnabled();
        _settings.StartMinimizedToTray = MinimizedToggle.IsChecked == true;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            Logger.Error("Saving settings failed.", ex);
        }
    }
}
