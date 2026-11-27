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

    private void OpenLibrary_Click(object sender, RoutedEventArgs e) => OpenLibrary(discover: false);
    private void OpenDiscover_Click(object sender, RoutedEventArgs e) => OpenLibrary(discover: true);

    /// <summary>Library and Discover are two tabs over one view: your wallpapers, or new ones online.</summary>
    private void OpenLibrary(bool discover)
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
            // A download in progress keeps Discover on screen until it finishes.
            if (discover) _libraryView.ShowDiscover();
            else if (_libraryView.DiscoverShown && !_libraryView.IsWorking) _libraryView.ShowCollection();
            discover = _libraryView.DiscoverShown;
            ShowPane(LibraryPane, discover ? DiscoverNavButton : LibraryNavButton, discover ? "/  Discover" : "/  Library");
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
        MorePopup.IsOpen = false;
        // The profile list is its own pane beside the profile editor; other sections use the whole width.
        var profilesVisible = selected == ProfilesNavButton;
        SidebarFrame.Visibility = profilesVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(profilesVisible ? (_shellCompact == true ? 200 : 236) : 0);
        ContentFrame.Margin = profilesVisible ? new Thickness(3, 0, 12, 12) : new Thickness(12, 0, 12, 12);
        foreach (var button in NavigationButtons)
            button.Tag = button == selected ? "Selected" : null;
        // Sections behind More light up the More pill and lend it their name.
        var inMore = MoreSections.Contains(selected);
        MoreNavButton.Tag = inMore ? "Selected" : null;
        if (inMore) MoreNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
        else MoreNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        MoreNavLabel.Text = inMore ? System.Windows.Automation.AutomationProperties.GetName(selected) is "Wallpaper switcher" ? "Switcher"
            : System.Windows.Automation.AutomationProperties.GetName(selected) : "More";
    }

    private System.Windows.Controls.Button[] MoreSections => new[]
    { TopBarNavButton, WorkspacesNavButton, TransitionsNavButton, TeamNavButton, SwitcherNavButton };

    private void MoreNav_Click(object sender, RoutedEventArgs e) => MorePopup.IsOpen = !MorePopup.IsOpen;

    private System.Windows.Controls.Button[] NavigationButtons => new[]
    { DashboardNavButton, CanvasNavButton, ProfilesNavButton, LibraryNavButton, DiscoverNavButton, TopBarNavButton, WorkspacesNavButton, TransitionsNavButton, TeamNavButton, SwitcherNavButton };

    private bool? _shellCompact;

    /// <summary>
    /// Fits the top bar to the window. When it runs out of room it lets go of extras one step at a time:
    /// the key hint and brand name, then the profile name, then the page names (icons stay, names move to tooltips).
    /// </summary>
    private void UpdateShellLayout()
    {
        var compact = ActualWidth < 1000;
        if (_shellCompact != compact)
        {
            _shellCompact = compact;
            if (SidebarFrame.Visibility == Visibility.Visible) SidebarColumn.Width = new GridLength(compact ? 200 : 236);
            foreach (var entry in _railEntries) entry.Monitor.ShowName = false;
        }
        var available = TopBar.ActualWidth;
        if (available <= 0) return;
        var level = 0;
        for (; level < 3; level++)
        {
            ApplyShellLevel(level);
            if (TopBarWidth() <= available) break;
        }
        ApplyShellLevel(level);
    }

    private double TopBarWidth()
    {
        var total = 0d;
        foreach (UIElement part in TopBar.Children)
        {
            if (part.Visibility == Visibility.Collapsed || part is System.Windows.Controls.Primitives.Popup) continue;
            part.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            total += part.DesiredSize.Width;
        }
        TopBar.InvalidateMeasure();
        return total + 8; // A little air so pills never touch.
    }

    private void ApplyShellLevel(int level)
    {
        SearchPillKeys.Visibility = BrandName.Visibility = level >= 1 ? Visibility.Collapsed : Visibility.Visible;
        ActivePillName.Visibility = level >= 2 ? Visibility.Collapsed : Visibility.Visible;
        var iconsOnly = level >= 3;
        foreach (var button in new[] { DashboardNavButton, ProfilesNavButton, CanvasNavButton, LibraryNavButton, DiscoverNavButton, MoreNavButton })
        {
            button.ToolTip ??= System.Windows.Automation.AutomationProperties.GetName(button);
            if (button.Content is not System.Windows.Controls.StackPanel content) continue;
            content.Children[1].Visibility = iconsOnly ? Visibility.Collapsed : Visibility.Visible;
            ((FrameworkElement)content.Children[0]).Margin = iconsOnly ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        }
    }

    private void EditActiveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator.ActiveProfileId is { } id) Select(id);
    }

    internal void OpenCanvas() => Dispatcher.BeginInvoke(DispatcherPriority.Background,
        new Action(() => ShowCanvas_Click(this, new RoutedEventArgs())));
}
