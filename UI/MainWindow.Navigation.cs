using System.Windows;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Persistence;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveDraft()) return;
        ShowProfiles();
        AddProfile();
    }

    private void ShowProfiles_Click(object sender, RoutedEventArgs e) => ShowProfiles();

    private void ShowProfiles()
    {
        if (!TryLeaveWidgetDraft()) return;
        ShowPane(ProfilePane, ProfilesNavButton, "/  Profiles");
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveWidgetDraft()) return;
        try
        {
            if (_libraryView == null)
            {
                var sources = _libraryStore.Exists ? Array.Empty<string>() : _coordinator.Profiles
                    .SelectMany(p => new[] { p.FolderPath }.Concat(p.AdditionalWallpaperPaths)).ToArray();
                var view = new LibraryWindow(_libraryStore, sources);
                LibraryPane.Content = view.CreateInlineContent(asset => AddLibraryWallpapers(new[] { asset }),
                    ShowProfiles, AddLibraryWallpapers);
                _libraryView = view;
            }
            _libraryView.SetTargetProfile(_selected?.Name);
            ShowPane(LibraryPane, LibraryNavButton, "/  Library");
        }
        catch (Exception ex)
        {
            Logger.Error("Opening wallpaper library failed.", ex);
            System.Windows.MessageBox.Show(this, $"The wallpaper library could not be opened:\n{ex.Message}",
                "Library unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowDashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void ShowDashboard()
    {
        if (!TryLeaveWidgetDraft()) return;
        ShowPane(DashboardPane, DashboardNavButton, "/  Overview");
        RefreshDashboard();
    }

    private void ShowCanvas_Click(object sender, RoutedEventArgs e)
    {
        CanvasPane.Content ??= new DesktopCanvasView(profiles: () => _coordinator.Profiles, activeProfile: () => _coordinator.ActiveProfileId, activeWallpaper: () => _coordinator.CurrentMediaPath);
        ShowPane(CanvasPane, CanvasNavButton, "/  Widgets");
    }

    private void ShowTopBar_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveWidgetDraft()) return;
        var page = TopBarPane.Content as TopBarPage;
        if (page == null) TopBarPane.Content = new TopBarPage(new TopBarStore(TopBarFile), StartTopBar);
        else page.Reload();
        ShowPane(TopBarPane, TopBarNavButton, "/  Top bar");
    }

    private void ShowWorkspaces_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveWidgetDraft()) return;
        var page = WorkspacesPane.Content as WorkspacesPage;
        if (page == null) WorkspacesPane.Content = new WorkspacesPage(new TopBarStore(TopBarFile), () => _coordinator.Profiles, StartTopBar);
        else page.Reload();
        ShowPane(WorkspacesPane, WorkspacesNavButton, "/  Workspaces");
    }

    private void ShowTransitions_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveWidgetDraft()) return;
        var page = TransitionsPane.Content as TransitionsPage;
        if (page == null) TransitionsPane.Content = new TransitionsPage(_settingsStore, _settings, PreviewPictures, () => _coordinator.Profiles.SelectMany(WallpaperEngine.GetProfileMedia).ToArray());
        else page.Reload();
        ShowPane(TransitionsPane, TransitionsNavButton, "/  Transitions");
    }

    private void ShowTeam_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveWidgetDraft()) return;
        if (TeamPane.Content is TeamPage page) page.Show(); else TeamPane.Content = new TeamPage(TopBarFile, StartTopBar);
        ShowPane(TeamPane, TeamNavButton, "/  Team");
    }

    internal Action OpenSwitcher { get; init; } = () => { };
    internal Func<string> WallpaperKeysChanged { get; init; } = () => "";

    private void ShowSwitcher_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveWidgetDraft()) return;
        // Built again each time, so that it shows the profile in use.
        SwitcherPane.Content = new SwitcherPage(_settingsStore, _settings, () => _coordinator.ActiveProfile is { } profile
            ? (WallpaperEngine.GetProfileMedia(profile), _coordinator.CurrentMediaPath) : (Array.Empty<string>(), ""), OpenSwitcher, WallpaperKeysChanged);
        ShowPane(SwitcherPane, SwitcherNavButton, "/  Wallpaper switcher");
    }

    private IEnumerable<string> PreviewPictures() => new[] { _coordinator.CurrentMediaPath ?? "" }
        .Concat(_coordinator.Profiles.OrderByDescending(profile => profile.Id == _coordinator.ActiveProfileId).SelectMany(WallpaperEngine.GetProfileMedia))
        .Where(path => WallpaperEngine.IsImageFile(path) && File.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2);

    internal string TopBarFile { get; init; } = TopBarProcess.DefaultFile;
    internal Action<string> StartTopBar { get; init; } = TopBarProcess.Start;
    internal void OpenTopBarSettings() => Dispatcher.BeginInvoke(DispatcherPriority.Background,
        new Action(() => ShowTopBar_Click(this, new RoutedEventArgs())));

    private void ComposeWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || !TryLeaveAllDrafts()) return;
        var profile = _selected.Id;
        var path = _previewMedia.Count > 0 ? _previewMedia[Math.Clamp(_previewIndex, 0, _previewMedia.Count - 1)] : "";
        ShowCanvas_Click(sender, e);
        if (CanvasPane.Content is DesktopCanvasView studio) studio.OpenWallpaper(profile, path);
    }

    private bool TryLeaveWidgetDraft() => CanvasPane.Visibility != Visibility.Visible
        || CanvasPane.Content is not DesktopCanvasView canvas || canvas.TryLeaveDraft();
    internal bool TryLeaveAllDrafts() => TryLeaveWidgetDraft() && TryLeaveDraft();

    private void ShowPane(FrameworkElement selectedPane, System.Windows.Controls.Button selectedButton, string title)
    {
        foreach (var pane in new FrameworkElement[] { ProfilePane, LibraryPane, DashboardPane, CanvasPane, TopBarPane, WorkspacesPane, TransitionsPane, TeamPane, SwitcherPane })
            pane.Visibility = pane == selectedPane ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in NavigationButtons)
        {
            if (button == selectedButton)
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
            else button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        }
        WorkspaceTitle.Text = title;
        UpdateNavigation(selectedButton);
    }

    private void UpdateNavigation(System.Windows.Controls.Button selected)
    {
        var profilesVisible = selected == ProfilesNavButton;
        ProfileSearchArea.Visibility = ProfileRailArea.Visibility = NewProfileButton.Visibility = profilesVisible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in NavigationButtons)
            button.Tag = button == selected ? "Selected" : null;
    }

    private System.Windows.Controls.Button[] NavigationButtons => new[]
    { DashboardNavButton, CanvasNavButton, ProfilesNavButton, LibraryNavButton, TopBarNavButton, WorkspacesNavButton, TransitionsNavButton, TeamNavButton, SwitcherNavButton };

    private bool? _shellCompact;

    private void UpdateShellLayout()
    {
        var compact = ActualWidth < 1000;
        if (_shellCompact == compact) return;
        _shellCompact = compact;
        SidebarColumn.Width = new GridLength(compact ? 80 : 200);
        ProfileSearchHint.Text = compact ? "\uE721" : "Search profiles";
        ProfileSearchHint.FontFamily = (System.Windows.Media.FontFamily)FindResource(compact ? "IconFont" : "AppFont");
        SidebarTop.Margin = compact ? new Thickness(8, 16, 8, 8) : new Thickness(16, 24, 16, 16);
        SidebarBottom.Margin = compact ? new Thickness(8) : new Thickness(16);
        SidebarHeading.Visibility = ProfileRailHeading.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in NavigationButtons.Append(SettingsButton))
        {
            button.ToolTip ??= System.Windows.Automation.AutomationProperties.GetName(button);
            if (button.Content is not System.Windows.Controls.StackPanel content) continue;
            content.Children[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ((FrameworkElement)content.Children[0]).Margin = compact ? new Thickness(0) : new Thickness(0, 0, 12, 0);
            button.HorizontalContentAlignment = compact ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left;
        }
        NewProfileButton.Content = compact ? "+" : "+  New profile";
        System.Windows.Automation.AutomationProperties.SetName(NewProfileButton, "New profile");
        ProfileRailArea.Margin = compact ? new Thickness(8, 0, 8, 0) : new Thickness(10, 0, 10, 0);
        foreach (var entry in _railEntries)
        {
            entry.Monitor.ShowName = false;
            entry.Card.Padding = compact ? new Thickness(4) : new Thickness(8);
            if (entry.Card.Child is System.Windows.Controls.Grid row)
                row.Children[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void EditActiveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator.ActiveProfileId is { } id) Select(id);
    }

    internal void OpenCanvas() => Dispatcher.BeginInvoke(DispatcherPriority.Background,
        new Action(() => ShowCanvas_Click(this, new RoutedEventArgs())));
}
