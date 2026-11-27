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
        UpdateNotice.ToolTip = updates.Status + " Open settings to update.";
        UpdateNotice.Visibility = SettingsUpdateButton.Visibility = AvailableReleaseNotesButton.Visibility = updates.Available != null ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesButton.IsEnabled = SettingsUpdateButton.IsEnabled = !updates.Busy;
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
        UiAppearance.SetDialogContent(dialog, scroll, "Settings", glass: true);
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

    private bool _loadingGlass;

    private void LoadGlassChoice()
    {
        _loadingGlass = true;
        GlassPicker.ItemsSource = Enum.GetValues<Persistence.GlassLevel>();
        GlassPicker.SelectedItem = UiAppearance.Glass;
        _loadingGlass = false;
        GlassHint.Text = UiAppearance.GlassSupported ? "" : "Needs Windows 11 with transparency effects on";
        GlassHint.Visibility = UiAppearance.GlassSupported ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Glass_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingGlass || GlassPicker.SelectedItem is not Persistence.GlassLevel level || level == UiAppearance.Glass) return;
        var previous = UiAppearance.Glass;
        try
        {
            var store = Persistence.ShellStore.Default;
            var shell = store.Load();
            shell.Glass = level;
            store.Save(shell);
            UiAppearance.Glass = level;
            UiAppearance.Apply(UiAppearance.Current.Name);
        }
        catch (Exception ex)
        {
            Logger.Error("Saving the glass setting failed.", ex);
            UiAppearance.Glass = previous;
            LoadGlassChoice();
        }
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }
        // Showing the saved state raises this too. Writing it then would point startup at whichever copy
        // was opened last, such as a build folder that cannot update itself.
        var autostart = AutostartToggle.IsChecked == true;
        if (autostart != AutostartManager.IsEnabled())
        {
            AutostartManager.SetEnabled(autostart);
            AutostartToggle.IsChecked = AutostartManager.IsEnabled();
        }
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
