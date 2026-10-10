using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WallpaperProfiles.Autostart;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Brush = System.Windows.Media.Brush;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Path = System.IO.Path;

namespace WallpaperProfiles.UI;

internal partial class MainWindow : Window
{
    private sealed record RailEntry(Guid Id, Border Card, MonitorThumbnail Monitor);
    private sealed class ProfileNavigationCard : Border
    {
        public required Action Open { get; init; }
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ProfileCardPeer(this);
        private sealed class ProfileCardPeer(ProfileNavigationCard card) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(card), System.Windows.Automation.Provider.IInvokeProvider
        {
            protected override string GetClassNameCore() => "Profile";
            protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Button;
            public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface pattern) =>
                pattern == System.Windows.Automation.Peers.PatternInterface.Invoke ? this : base.GetPattern(pattern);
            public void Invoke() => card.Dispatcher.BeginInvoke(card.Open);
        }
    }

    private readonly WallpaperCoordinator _coordinator;
    private readonly ProfileStore _store;
    private readonly SettingsStore _settingsStore;
    private readonly LibraryStore _libraryStore;
    private readonly ProfileShortcutManager? _profileShortcuts;
    private Guid? _shortcutProfileId;
    private string _pendingShortcut = "";
    private AppSettings _settings;
    private bool _loadingTheme = true;

    private readonly List<RailEntry> _railEntries = new();
    private Guid? _selectedId;
    private WallpaperProfile? _selected;
    private bool _loadingDraft;
    private string _savedDraft = "";
    internal Func<DraftChoice>? ChooseDraftAction { get; set; }
    private Window? _settingsDialog;
    private LibraryWindow? _libraryView;
    private MonitorThumbnail? _activityPreviewMonitor;
    private IReadOnlyList<string> _previewMedia = Array.Empty<string>();
    private int _previewIndex;
    private int _previewRequest;
    private readonly ObservableCollection<WallpaperAsset> _additionalWallpapers = new();

    private readonly DispatcherTimer _pathDebounce;
    private readonly DispatcherTimer _dashboardTimer;
    private int _pathVersion;

    private sealed record OverrideDuration(string Label, TimeSpan Duration);

    public MainWindow(WallpaperCoordinator coordinator, ProfileStore store, SettingsStore settingsStore, LibraryStore? libraryStore = null, ProfileShortcutManager? profileShortcuts = null)
    {
        InitializeComponent();
        foreach (FrameworkElement pane in new FrameworkElement[] { ProfilePane, DashboardPane, LibraryPane, CanvasPane })
            pane.IsVisibleChanged += (_, e) => { if (e.NewValue is true) AnimatePane(pane); };

        _coordinator = coordinator;
        _profileShortcuts = profileShortcuts;
        ShortcutButton.IsEnabled = profileShortcuts != null;
        _store = store;
        _settingsStore = settingsStore;
        _libraryStore = libraryStore ?? new LibraryStore(Path.Combine(AppPaths.BaseDir, "library.json"));
        _settings = coordinator.Settings;
        if (UiAppearance.Current.Name != UiAppearance.Find(_settings.UiTheme).Name) UiAppearance.Apply(_settings.UiTheme);
        TrySetAppIcon();
        ThemePicker.ItemsSource = UiAppearance.Themes;
        ThemePicker.SelectedItem = UiAppearance.Find(_settings.UiTheme);
        _loadingTheme = false;
        StateChanged += (_, _) => UpdateMaximizeButton();

        _pathDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _pathDebounce.Tick += (_, _) =>
        {
            _pathDebounce.Stop();
            ValidatePath();
        };
        FolderBox.TextChanged += (_, _) =>
        {
            ++_pathVersion;
            _pathDebounce.Stop();
            _pathDebounce.Start();
        };
        AdditionalWallpaperList.ItemsSource = _additionalWallpapers;
        _additionalWallpapers.CollectionChanged += (_, _) =>
        {
            AdditionalWallpaperPanel.Visibility = _additionalWallpapers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AdditionalWallpaperCount.Text = $"{_additionalWallpapers.Count} additional {(_additionalWallpapers.Count == 1 ? "wallpaper" : "wallpapers")}";
            ++_pathVersion;
            _pathDebounce.Stop();
            _pathDebounce.Start();
        };

        OverrideDurationCombo.ItemsSource = new[]
        {
            new OverrideDuration("15 minutes", TimeSpan.FromMinutes(15)),
            new OverrideDuration("30 minutes", TimeSpan.FromMinutes(30)),
            new OverrideDuration("1 hour", TimeSpan.FromHours(1)),
            new OverrideDuration("2 hours", TimeSpan.FromHours(2))
        };
        OverrideDurationCombo.SelectedIndex = 2;
        _dashboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _dashboardTimer.Tick += (_, _) => RefreshDashboard();
        DashboardPane.IsVisibleChanged += (_, _) => _dashboardTimer.IsEnabled = DashboardPane.IsVisible;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _dashboardTimer.Stop();
            _pathDebounce.Stop();
            _coordinator.StateChanged -= CoordinatorStateChanged;
            _libraryView?.Close();
        };
        _coordinator.StateChanged += CoordinatorStateChanged;
        RefreshDashboard();
        foreach (var box in new[] { NameBox, FolderBox, IntervalBox }) box.TextChanged += (_, _) => UpdateDraftState();
        FitCombo.SelectionChanged += (_, _) => { UpdateDraftState(); UpdatePreviewFit(); };
        foreach (var box in new[] { RandomBox, VideoMuteBox, IconSafeBox })
        { box.Checked += (_, _) => UpdateDraftState(); box.Unchecked += (_, _) => UpdateDraftState(); }
        _additionalWallpapers.CollectionChanged += (_, _) => UpdateDraftState();
        SizeChanged += (_, _) => UpdatePreviewFit();
        ProfilePane.SizeChanged += (_, _) => UpdatePreviewFit();
        UpdateDraftState();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        AutostartToggle.IsChecked = AutostartManager.IsEnabled();
        MinimizedToggle.IsChecked = _settings.StartMinimizedToTray;
        BuildRail();
        _loadingDraft = false;
        UpdateDraftState();
        if (_railEntries.Count > 0)
        {
            Select(_railEntries[0].Id);
        }
        ShowDashboard();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!App.IsExiting && !System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            if (TryLeaveWidgetDraft() && TryLeaveDraft()) Hide();
        }
    }

    private void TrySetAppIcon()
    {
        UiAppearance.Attach(this);
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2) Maximize_Click(sender, e);
            else DragMove();
        }
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void UpdateMaximizeButton()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Data = Geometry.Parse(maximized ? "M0,3 L7,3 7,10 0,10 Z M3,3 L3,0 10,0 10,7 7,7" : "M0,0 L10,0 10,10 0,10 Z");
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    { if (TryLeaveWidgetDraft() && TryLeaveDraft()) Hide(); }

    private System.Windows.Media.Brush ThemeBrush(string key) => (System.Windows.Media.Brush)FindResource(key);

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
        CanvasPane.Visibility = Visibility.Collapsed;
        CanvasNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        ProfilePane.Visibility = Visibility.Visible;
        LibraryPane.Visibility = Visibility.Collapsed;
        DashboardPane.Visibility = Visibility.Collapsed;
        ProfilesNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
        LibraryNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        DashboardNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        WorkspaceTitle.Text = "/  Profiles";
        UpdateNavigation(ProfilesNavButton);
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveWidgetDraft()) return;
        CanvasPane.Visibility = Visibility.Collapsed;
        CanvasNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
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
            ProfilePane.Visibility = Visibility.Collapsed;
            DashboardPane.Visibility = Visibility.Collapsed;
            LibraryPane.Visibility = Visibility.Visible;
            LibraryNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
            ProfilesNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            DashboardNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            WorkspaceTitle.Text = "/  Library";
            UpdateNavigation(LibraryNavButton);
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
        CanvasPane.Visibility = Visibility.Collapsed;
        CanvasNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        DashboardPane.Visibility = Visibility.Visible;
        ProfilePane.Visibility = Visibility.Collapsed;
        LibraryPane.Visibility = Visibility.Collapsed;
        DashboardNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
        ProfilesNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        LibraryNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        WorkspaceTitle.Text = "/  Overview";
        UpdateNavigation(DashboardNavButton);
        RefreshDashboard();
    }

    private void ShowCanvas_Click(object sender, RoutedEventArgs e)
    {
        CanvasPane.Content ??= new DesktopCanvasView(profiles: () => _coordinator.Profiles, activeProfile: () => _coordinator.ActiveProfileId);
        CanvasPane.Visibility = Visibility.Visible;
        ProfilePane.Visibility = Visibility.Collapsed;
        LibraryPane.Visibility = Visibility.Collapsed;
        DashboardPane.Visibility = Visibility.Collapsed;
        CanvasNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
        ProfilesNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        LibraryNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        DashboardNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        WorkspaceTitle.Text = "/  Widgets";
        UpdateNavigation(CanvasNavButton);
    }

    private bool TryLeaveWidgetDraft() => CanvasPane.Visibility != Visibility.Visible
        || CanvasPane.Content is not DesktopCanvasView canvas || canvas.TryLeaveDraft();
    internal bool TryLeaveAllDrafts() => TryLeaveWidgetDraft() && TryLeaveDraft();

    private void UpdateNavigation(System.Windows.Controls.Button selected)
    {
        var profilesVisible = selected == ProfilesNavButton;
        ProfileSearchArea.Visibility = ProfileRailArea.Visibility = NewProfileButton.Visibility = profilesVisible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { DashboardNavButton, CanvasNavButton, ProfilesNavButton, LibraryNavButton })
            button.Tag = button == selected ? "Selected" : null;
    }

    private static void AnimatePane(FrameworkElement pane)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var duration = TimeSpan.FromMilliseconds(180);
        pane.BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1, duration) { FillBehavior = FillBehavior.Stop });
        var slide = new TranslateTransform();
        pane.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private void EditActiveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator.ActiveProfileId is { } id) Select(id);
    }

    internal void OpenCanvas() => Dispatcher.BeginInvoke(DispatcherPriority.Background,
        new Action(() => ShowCanvas_Click(this, new RoutedEventArgs())));

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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (RailStack != null && SearchEmpty != null)
        {
            FilterRail();
        }
    }

    private void SceneGallery_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The horizontal gallery otherwise consumes the page's vertical wheel input.
        DashboardPane.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
            Source = DashboardPane
        });
        e.Handled = true;
    }

    private void FilterRail()
    {
        var query = SearchBox.Text.Trim();
        var visible = 0;
        foreach (var entry in _railEntries)
        {
            var matches = entry.Monitor.ProfileName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            entry.Card.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches) visible++;
        }
        SearchEmpty.Visibility = visible == 0 && query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ============ Rail ============

    private void BuildRail()
    {
        RailStack.Children.Clear();
        SceneGallery.Children.Clear();
        _railEntries.Clear();
        SceneGalleryEmpty.Visibility = _coordinator.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_coordinator.Profiles.Count == 0)
        {
            RailStack.Children.Add(new TextBlock
            {
                Text = "No profiles yet.\nAdd one below.",
                Foreground = ThemeBrush("TextTertiaryBrush"),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 10),
                Opacity = 0.9,
            });
        }

        foreach (var profile in _coordinator.Profiles)
        {
            var monitor = new MonitorThumbnail
            {
                Width = 162,
                BezelWidth = 64,
                BezelHeight = 44,
                ShowName = true,
                ProfileName = profile.Name,
                HasRules = profile.Schedule.Count > 0 || profile.EventTriggers.Count > 0,
                IsActive = profile.Id == _coordinator.ActiveProfileId,
            };

            var status = new TextBlock { Text = (profile.Id == _coordinator.ActiveProfileId ? "Applied" : "Not applied")
                + (profile.Schedule.Count + profile.EventTriggers.Count > 0 ? " · Automated" : ""),
                FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var card = new ProfileNavigationCard
            {
                Open = () => Select(profile.Id),
                Margin = new Thickness(0, 0, 0, 6),
                Style = (Style)FindResource("ProfileCard"),
                Focusable = true,
                Child = new StackPanel { Children = { monitor, status } },
            };
            System.Windows.Automation.AutomationProperties.SetName(card, $"Open profile {profile.Name}");
            card.MouseLeftButtonUp += (_, _) => Select(profile.Id);
            card.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                Select(profile.Id);
                e.Handled = true;
            };
            RailStack.Children.Add(card);
            _railEntries.Add(new RailEntry(profile.Id, card, monitor));
            AddSceneCard(profile, monitor);

            _ = LoadRailThumbAsync(profile, monitor);
        }

        FilterRail();
    }

    private void AddSceneCard(WallpaperProfile profile, MonitorThumbnail monitor)
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var artwork = new Grid { Height = 118 };
        var fallback = new Border { CornerRadius = new CornerRadius(13, 13, 0, 0),
            Background = new LinearGradientBrush(System.Windows.Media.Color.FromRgb(29, 82, 80),
                System.Windows.Media.Color.FromRgb(150, 177, 137), 35) };
        artwork.Children.Add(fallback);
        var image = new ImageBrush { Stretch = Stretch.UniformToFill };
        BindingOperations.SetBinding(image, ImageBrush.ImageSourceProperty,
            new System.Windows.Data.Binding(nameof(MonitorThumbnail.ScreenSource)) { Source = monitor });
        artwork.Children.Add(new Border { CornerRadius = new CornerRadius(13, 13, 0, 0), Background = image });
        var placeholder = new TextBlock { Text = "Add a wallpaper  →", Foreground = System.Windows.Media.Brushes.White,
            FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center, IsHitTestVisible = false };
        var placeholderStyle = new Style(typeof(TextBlock));
        placeholderStyle.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        var empty = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(MonitorThumbnail.ScreenSource)) { Source = monitor }, Value = null };
        empty.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        placeholderStyle.Triggers.Add(empty); placeholder.Style = placeholderStyle;
        artwork.Children.Add(placeholder);
        var preview = new System.Windows.Controls.Button { Content = artwork, Tag = profile.Id,
            Style = (Style)FindResource("ScenePreviewButton"), ToolTip = $"Open {profile.Name} in the wallpaper studio" };
        preview.Click += (_, _) => Select(profile.Id);
        System.Windows.Automation.AutomationProperties.SetName(preview, $"Preview {profile.Name}");
        content.Children.Add(preview);
        var footer = new Grid { Margin = new Thickness(13) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold,
            FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0), ToolTip = profile.Name };
        footer.Children.Add(title);
        var play = new System.Windows.Controls.Button { Padding = new Thickness(10, 6, 10, 6),
            FontSize = 12, ToolTip = $"Apply {profile.Name} to your desktop", Tag = profile.Id };
        var style = new Style(typeof(System.Windows.Controls.Button), (Style)FindResource("PrimaryButton"));
        var active = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(MonitorThumbnail.IsActive)) { Source = monitor }, Value = true };
        active.Setters.Add(new Setter(System.Windows.Controls.ContentControl.ContentProperty, "Applied"));
        style.Triggers.Add(active);
        style.Setters.Add(new Setter(System.Windows.Controls.ContentControl.ContentProperty, "Apply"));
        play.Style = style;
        play.Click += (_, _) => _coordinator.SwitchManually(profile.Id);
        System.Windows.Automation.AutomationProperties.SetName(play, $"Apply profile {profile.Name}");
        Grid.SetColumn(play, 1); footer.Children.Add(play);
        Grid.SetRow(footer, 1); content.Children.Add(footer);
        SceneGallery.Children.Add(new Border { Width = 214, Margin = new Thickness(0, 0, 12, 0),
            Style = (Style)FindResource("SceneCard"), Child = content });
    }

    private static async Task LoadRailThumbAsync(WallpaperProfile profile, MonitorThumbnail monitor)
    {
        var preview = await Task.Run(() =>
        {
            var first = WallpaperEngine.GetProfileMedia(profile).FirstOrDefault();
            return (Path: first, Image: ThumbnailLoader.Load(first, 320));
        });
        if (monitor.Dispatcher.HasShutdownStarted) return;
        await monitor.Dispatcher.InvokeAsync(() =>
        {
            monitor.IsVideo = preview.Path != null && WallpaperEngine.IsLiveFile(preview.Path);
            monitor.ScreenSource = preview.Image;
        });
    }

    private void Select(Guid id) => SelectProfile(id, false);

    private void SelectProfile(Guid id, bool discard)
    {
        if (!discard && _selectedId != id && !TryLeaveDraft()) return;
        if (!TryLeaveWidgetDraft()) return;
        if (!discard && _selectedId == id && IsDraftDirty) { ShowProfiles(); return; }
        _loadingDraft = true;
        _selectedId = id;
        var source = _coordinator.Profiles.FirstOrDefault(p => p.Id == id);
        _selected = source == null ? null : DraftGuard.Clone(source);
        if (_selected == null)
        {
            _loadingDraft = false; return;
        }
        ShowProfiles();
        foreach (var entry in _railEntries)
        {
            if (entry.Id == id)
            {
                entry.Card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                entry.Card.SetResourceReference(Border.BackgroundProperty, "AccentSubtleBrush");
            }
            else
            {
                entry.Card.ClearValue(Border.BorderBrushProperty);
                entry.Card.ClearValue(Border.BackgroundProperty);
            }
        }

        NameBox.Text = _selected.Name;
        UpdateShortcutButton();
        _additionalWallpapers.Clear();
        foreach (var path in _selected.AdditionalWallpaperPaths)
            _additionalWallpapers.Add(new WallpaperAsset { FilePath = path, Name = Path.GetFileNameWithoutExtension(path) });
        FolderBox.Text = _selected.FolderPath;
        FitCombo.SelectedItem = _selected.FitMode;
        IntervalBox.Text = _selected.SlideshowIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        RandomBox.IsChecked = _selected.SlideshowRandom;
        VideoMuteBox.IsChecked = _selected.VideoMuted;
        IconSafeBox.IsChecked = _selected.IconFriendlyLive;
        ScheduleSummary.Text = _selected.Schedule.Count > 0
            ? string.Join(Environment.NewLine, _selected.Schedule.Select(DescribeSchedule)) : "No schedules";
        TriggerSummary.Text = _selected.EventTriggers.Count > 0
            ? string.Join(Environment.NewLine, _selected.EventTriggers.Select(t => $"{t.Describe()} · priority {t.Priority}")) : "No triggers";
        EditRulesButton.Content = _selected.Schedule.Count + _selected.EventTriggers.Count > 0 ? "Edit schedules & triggers" : "Add schedules or triggers";
        SceneSummary.Text = (_selected.SceneAccent.Length > 0 ? "Scene accent: " + _selected.SceneAccent : "Default accent")
            + (_selected.AmbientAudioPath.Length > 0 ? " · ambient: " + Path.GetFileNameWithoutExtension(_selected.AmbientAudioPath) : " · no ambient track");
        _loadingDraft = false;
        _savedDraft = DraftSnapshot();
        ProfileMessage.Text = _selected.Id == _coordinator.ActiveProfileId ? "Currently applied to your desktop" : "Editing profile · Apply to use it on your desktop";
        UpdateDraftState(); UpdatePreviewFit();
    }

    private string DraftSnapshot() => DraftGuard.Snapshot(new { NameBox.Text, Path = FolderBox.Text,
        Interval = IntervalBox.Text, Fit = FitCombo.SelectedItem, Random = RandomBox.IsChecked,
        Muted = VideoMuteBox.IsChecked, Icons = IconSafeBox.IsChecked,
        Additional = _additionalWallpapers.Select(a => a.FilePath).ToArray() });
    internal bool IsDraftDirty => !_loadingDraft && _selected != null && _savedDraft != DraftSnapshot();
    private void UpdateDraftState()
    {
        if (_loadingDraft) return;
        SaveProfileButton.IsEnabled = IsDraftDirty;
        ApplyProfileButton.IsEnabled = MoreProfileButton.IsEnabled = _selected != null;
        ShortcutButton.IsEnabled = _profileShortcuts != null && _selected != null;
        ApplyProfileButton.Content = IsDraftDirty ? "Save & apply" : "Apply to desktop";
        if (IsDraftDirty) ProfileMessage.Text = "Unsaved changes";
        else if (ProfileMessage.Text == "Unsaved changes")
            ProfileMessage.Text = _selected?.Id == _coordinator.ActiveProfileId ? "Currently applied to your desktop" : "Editing profile · Apply to use it on your desktop";
        SourceNameText.Text = string.IsNullOrWhiteSpace(FolderBox.Text) ? "No source selected"
            : Path.GetFileName(FolderBox.Text.Trim().TrimEnd(Path.DirectorySeparatorChar));
        SourceNameText.ToolTip = FolderBox.Text;
    }
    internal bool TryLeaveDraft()
    {
        if (!IsDraftDirty) return true;
        var id = _selectedId!.Value;
        return DraftGuard.Confirm(this, $"profile '{_selected!.Name}'", SaveDraft,
            () => SelectProfile(id, true), ChooseDraftAction);
    }
    private bool SaveDraft()
    {
        if (_selected == null) return false;
        var draft = DraftGuard.Clone(_selected);
        if (!CollectFields(draft) || !TrySaveProfile(draft)) return false;
        _coordinator.ProfilesEdited();
        _savedDraft = DraftSnapshot();
        BuildRail(); SelectProfile(draft.Id, true);
        ProfileMessage.Text = "Changes saved";
        return true;
    }
    private void MoreProfile_Click(object sender, RoutedEventArgs e)
    {
        var menu = MoreProfileButton.ContextMenu;
        menu.PlacementTarget = MoreProfileButton; menu.IsOpen = true;
    }
    private void UpdatePreviewFit()
    {
        if (PreviewMon == null || PreviewFrame == null) return;
        DesktopContextButton.Tag = ActualHeight < 760 ? "Compact" : null;
        var fit = FitCombo.SelectedItem is FitMode selected ? selected : FitMode.Fill;
        var screen = fit == FitMode.Span ? System.Windows.Forms.SystemInformation.VirtualScreen : System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var ratio = screen.Width / (double)screen.Height;
        var height = Math.Min(ActualHeight < 720 ? 170d : 280d, Math.Max(200, ProfilePane.ActualWidth - 50) / ratio);
        PreviewFrame.Height = height;
        PreviewMon.BezelHeight = height; PreviewMon.BezelWidth = height * ratio;
        PreviewMon.PreviewFit = fit;
    }
    private bool InvalidField(string message, System.Windows.Controls.Control field, int tab)
    {
        ShowProfiles(); ProfileTabs.SelectedIndex = tab; ProfileMessage.Text = message;
        DraftGuard.Focus(field); return false;
    }

    private void UpdateShortcutButton()
    {
        var shortcut = _selected?.KeyboardShortcut;
        ShortcutButton.Content = string.IsNullOrWhiteSpace(shortcut) ? "Assign shortcut" : shortcut.Replace("+", " + ");
        ShortcutButton.ToolTip = _selected == null ? "Select a profile" : _profileShortcuts?.GetError(_selected.Id)
            ?? "Switch to this profile from anywhere, including the tray";
    }

    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _profileShortcuts == null) return;
        _shortcutProfileId = _selected.Id;
        _pendingShortcut = _selected.KeyboardShortcut;
        ShortcutCapture.Text = _pendingShortcut.Replace("+", " + ");
        ShortcutMessage.Text = _profileShortcuts.GetError(_selected.Id) ?? "Works while Belie is in the tray.";
        _profileShortcuts.IsCapturing = true;
        ShortcutPopup.IsOpen = true;
        Dispatcher.BeginInvoke(() => { if (ShortcutPopup.IsOpen) ShortcutCapture.Focus(); }, DispatcherPriority.Input);
    }

    private void ShortcutCapture_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab && (Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)) { e.Handled = false; return; }
        if (key == Key.Escape) { CloseShortcut(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        { _pendingShortcut = ""; ShortcutCapture.Text = ""; return; }
        string text;
        try { text = ProfileShortcutManager.Format(new KeyGesture(key, Keyboard.Modifiers)); }
        catch (ArgumentException) { ShortcutMessage.Text = "Include Ctrl or Alt in your shortcut."; return; }
        if (!ProfileShortcutManager.TryParse(text, out _, out var error)) { ShortcutMessage.Text = error; return; }
        _pendingShortcut = text;
        ShortcutCapture.Text = text.Replace("+", " + ");
        ShortcutMessage.Text = "Press Assign to save this shortcut.";
    }

    private void AssignShortcut_Click(object sender, RoutedEventArgs e) => SaveShortcut(_pendingShortcut);
    private void RemoveShortcut_Click(object sender, RoutedEventArgs e) => SaveShortcut("");
    private void CancelShortcut_Click(object sender, RoutedEventArgs e) => CloseShortcut();
    private void CloseShortcut()
    {
        ShortcutPopup.IsOpen = false;
        if (_profileShortcuts != null) _profileShortcuts.IsCapturing = false;
    }
    private void ShortcutPopup_Closed(object sender, EventArgs e)
    { if (_profileShortcuts != null) _profileShortcuts.IsCapturing = false; }
    private void SaveShortcut(string shortcut)
    {
        if (_profileShortcuts == null || _shortcutProfileId is not Guid id) return;
        if (!_profileShortcuts.TryAssign(id, shortcut, out var error)) { ShortcutMessage.Text = error; return; }
        if (_selected?.Id == id) _selected.KeyboardShortcut = _coordinator.Profiles.First(p => p.Id == id).KeyboardShortcut;
        UpdateShortcutButton();
        CloseShortcut();
    }

    private static string DescribeSchedule(ScheduleRule rule)
    {
        var days = rule.DaysOfWeek.Count == 7 ? "Every day"
            : rule.DaysOfWeek.Count == 0 ? "No days selected"
            : string.Join(", ", rule.DaysOfWeek.OrderBy(day => ((int)day + 6) % 7)
                .Select(day => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day)));
        var duration = rule.StartTime == rule.EndTime ? " · inactive" : rule.StartTime > rule.EndTime ? " · overnight" : "";
        return $"{days} · {rule.StartTime:HH:mm} – {rule.EndTime:HH:mm}{duration}";
    }

    private void RefreshActiveGlow()
    {
        foreach (var entry in _railEntries)
        {
            var profile = _coordinator.Profiles.FirstOrDefault(p => p.Id == entry.Id);
            entry.Monitor.IsActive = profile != null && profile.Id == _coordinator.ActiveProfileId;
            if (entry.Card.Child is StackPanel panel && panel.Children[1] is TextBlock state)
                state.Text = (entry.Monitor.IsActive ? "Applied" : "Not applied") + (entry.Monitor.HasRules ? " · Automated" : "");
        }
    }

    // ============ Path validation & preview ============

    private async void ValidatePath()
    {
        if (Dispatcher.HasShutdownStarted) return;
        try { await Dispatcher.InvokeAsync(ValidatePathAsync).Task.Unwrap(); }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted) { }
    }

    private async Task ValidatePathAsync()
    {
        var path = FolderBox.Text.Trim();
        var version = ++_pathVersion;
        ++_previewRequest;
        SetPreviewMedia(Array.Empty<string>());
        if (path.Length > 0 || _additionalWallpapers.Count > 0)
        { PreviewEmptyTitle.Text = "Loading preview…"; PreviewEmptyHint.Text = "You can keep editing while it loads."; }
        else
        { PreviewEmptyTitle.Text = "Choose a wallpaper"; PreviewEmptyHint.Text = "Drop an image or video here to get started."; }

        if (_additionalWallpapers.Count > 0)
        {
            var extraPaths = _additionalWallpapers.Select(a => a.FilePath).ToList();
            var media = await Task.Run(() => WallpaperEngine.GetProfileMedia(new WallpaperProfile
                { FolderPath = path, AdditionalWallpaperPaths = extraPaths }));
            if (version != _pathVersion) return;
            var missing = extraPaths.Count(source => !File.Exists(source));
            SetPathStatus(media.Count > 0 ? "GoodBrush" : "DangerBrush",
                $"{media.Count} {(media.Count == 1 ? "wallpaper" : "wallpapers")}" + (missing > 0 ? $" · {missing} missing" : ""));
            var first = media.FirstOrDefault();
            var thumbnail = await Task.Run(() => ThumbnailLoader.Load(first, 900));
            if (version != _pathVersion) return;
            PreviewMon.PreviewFilePath = first ?? "";
            PreviewMon.ScreenSource = thumbnail;
            if (thumbnail == null && first != null) { PreviewEmptyTitle.Text = "Preview unavailable"; PreviewEmptyHint.Text = "The source is available. Choose another image to preview."; }
            PreviewMon.IsVideo = first != null && WallpaperEngine.IsLiveFile(first);
            SetPreviewMedia(media);
            return;
        }

        if (path.Length == 0)
        {
            SetPathStatus("TextTertiaryBrush", "No location set");
            PreviewMon.ScreenSource = null;
            return;
        }

        var info = await Task.Run(() => InspectPath(path));
        if (version != _pathVersion)
        {
            return;
        }

        if (!info.Exists)
        {
            SetPathStatus("DangerBrush", "Path not found");
            PreviewMon.ScreenSource = null;
            PreviewMon.IsVideo = false;
            return;
        }

        if (info.IsFile)
        {
            if (info.VideoCount > 0)
            {
                SetPathStatus("GoodBrush", "Single video · live wallpaper");
            }
            else if (info.ImageCount > 0)
            {
                SetPathStatus("GoodBrush", "Single image");
            }
            else
            {
                SetPathStatus("DangerBrush", "Not an image or video");
            }
        }
        else if (info.ImageCount == 1 && info.VideoCount == 0)
        {
            SetPathStatus("GoodBrush", "Folder · 1 image");
        }
        else if (info.ImageCount == 0 && info.VideoCount > 0)
        {
            SetPathStatus("DangerBrush",
                $"Folder · {info.VideoCount} video(s) — pick a video file directly for a live wallpaper");
        }
        else
        {
            var text = $"Folder · {info.ImageCount} images";
            if (info.VideoCount > 0)
            {
                text += $" · {info.VideoCount} video(s)";
            }
            SetPathStatus("GoodBrush", text);
        }

        var image = info.FirstMedia is null
            ? null
            : await Task.Run(() => ThumbnailLoader.Load(info.FirstMedia, 900));
        if (version != _pathVersion)
        {
            return;
        }
        var previous = PreviewMon.ScreenSource;
        PreviewMon.PreviewFilePath = info.FirstMedia ?? "";
        PreviewMon.ScreenSource = image;
        if (image == null && PreviewMon.PreviewFilePath.Length > 0) { PreviewEmptyTitle.Text = "Preview unavailable"; PreviewEmptyHint.Text = "The source is available. Choose another image to preview."; }
        PreviewMon.IsVideo = info.IsFile && info.VideoCount > 0;
        if (image != null && !ReferenceEquals(previous, image))
        {
            PreviewMon.Pulse();
        }
        var available = await Task.Run(() => WallpaperEngine.GetProfileMedia(new WallpaperProfile { FolderPath = path }));
        if (version == _pathVersion) SetPreviewMedia(available);
    }

    private void SetPreviewMedia(IReadOnlyList<string> media)
    {
        _previewMedia = media;
        _previewIndex = 0;
        PreviousPreviewButton.IsEnabled = NextPreviewButton.IsEnabled = media.Count > 1;
        PreviewPositionText.Text = media.Count > 0 ? $"1 / {media.Count}" : "Desktop preview";
    }

    private async void BrowsePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewMedia.Count < 2 || sender is not System.Windows.Controls.Button { Tag: string step }) return;
        _previewIndex = (_previewIndex + int.Parse(step, CultureInfo.InvariantCulture) + _previewMedia.Count) % _previewMedia.Count;
        var request = ++_previewRequest;
        var version = _pathVersion;
        var file = _previewMedia[_previewIndex];
        PreviewPositionText.Text = $"{_previewIndex + 1} / {_previewMedia.Count}";
        var image = await Task.Run(() => ThumbnailLoader.Load(file, 1200));
        if (request != _previewRequest || version != _pathVersion) return;
        PreviewMon.PreviewFilePath = file;
        PreviewMon.ScreenSource = image;
        if (image == null && PreviewMon.PreviewFilePath.Length > 0) { PreviewEmptyTitle.Text = "Preview unavailable"; PreviewEmptyHint.Text = "The source is available. Choose another image to preview."; }
        PreviewMon.IsVideo = WallpaperEngine.IsLiveFile(file);
        if (SystemParameters.ClientAreaAnimation) PreviewMon.Pulse();
    }

    private void ExpandPreview_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewMon.ScreenSource == null) return;
        var content = new Grid { Background = System.Windows.Media.Brushes.Black };
        content.Children.Add(new System.Windows.Controls.Image { Source = PreviewMon.ScreenSource, Stretch = Stretch.Uniform });
        var preview = new Window { Owner = this, Title = "Wallpaper preview", WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized, Content = content, Background = System.Windows.Media.Brushes.Black };
        var close = new System.Windows.Controls.Button { Content = "Close preview  ×", HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top, Margin = new Thickness(24), Padding = new Thickness(16, 10, 16, 10),
            Style = (Style)FindResource("PrimaryButton") };
        close.Click += (_, _) => preview.Close();
        content.Children.Add(close);
        preview.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) preview.Close(); };
        preview.ShowDialog();
    }

    private static (bool Exists, bool IsFile, int ImageCount, int VideoCount, string? FirstMedia) InspectPath(string path)
        => PathInspector.Inspect(path);

    private void SetPathStatus(string brushKey, string text)
    {
        PathStatusDot.Fill = (Brush)FindResource(brushKey);
        PathStatusText.Text = text;
        if (brushKey == "DangerBrush")
        { PreviewEmptyTitle.Text = "Source unavailable"; PreviewEmptyHint.Text = text + ". Choose another source or restore the file."; }
    }

    // ============ Pickers & drag-drop ============

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var current = FolderBox.Text.Trim();
        var start = current.Length > 0 ? current : _selected?.FolderPath;
        var picked = ModernFolderPicker.Pick(this, "Choose a wallpaper folder", start);
        if (picked != null)
        {
            FolderBox.Text = picked;
        }
    }

    private void PickImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a wallpaper image",
            Filter = DropHelper.ImageFileFilter,
        };
        var current = FolderBox.Text.Trim();
        if (File.Exists(current))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }
        else if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }
        if (dialog.ShowDialog() == true)
        {
            FolderBox.Text = dialog.FileName;
        }
    }

    private void PickVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a live wallpaper video",
            Filter = DropHelper.VideoFileFilter,
        };
        var current = FolderBox.Text.Trim();
        if (File.Exists(current))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }
        else if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }
        if (dialog.ShowDialog() == true)
        {
            FolderBox.Text = dialog.FileName;
        }
    }

    private void DropZone_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (DropHelper.CanAccept(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void DropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        var path = DropHelper.ExtractPath(e.Data);
        if (path != null)
        {
            FolderBox.Text = path;
        }
    }

    // ============ Editing ============

    private bool CollectFields(WallpaperProfile? target = null)
    {
        var profile = target ?? _selected;
        if (profile == null)
        {
            return false;
        }
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            return InvalidField("Enter a profile name before saving.", NameBox, 0);
        }
        if (!int.TryParse(IntervalBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval)
            || interval < 0 || interval > 1440)
        {
            return InvalidField("Slideshow interval must be a whole number between 0 and 1440 minutes.", IntervalBox, 1);
        }

        profile.Name = name;
        profile.FolderPath = FolderBox.Text.Trim();
        profile.AdditionalWallpaperPaths = _additionalWallpapers.Select(a => a.FilePath)
            .Where(path => !path.Equals(profile.FolderPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        profile.FitMode = (FitMode)(FitCombo.SelectedItem ?? FitMode.Fill);
        profile.SlideshowIntervalMinutes = interval;
        profile.SlideshowRandom = RandomBox.IsChecked == true;
        profile.VideoMuted = VideoMuteBox.IsChecked != false;
        profile.IconFriendlyLive = IconSafeBox.IsChecked == true;
        return true;
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || !TryLeaveDraft()) return;

        var copy = System.Text.Json.JsonSerializer.Deserialize<WallpaperProfile>(
            System.Text.Json.JsonSerializer.Serialize(_selected))!;
        if (!CollectFields(copy)) return;

        copy.Id = Guid.NewGuid();
        copy.KeyboardShortcut = "";
        var baseName = copy.Name + " copy";
        copy.Name = baseName;
        for (var suffix = 2; _coordinator.Profiles.Any(p =>
                 string.Equals(p.Name, copy.Name, StringComparison.CurrentCultureIgnoreCase)); suffix++)
        {
            copy.Name = $"{baseName} {suffix}";
        }
        foreach (var rule in copy.Schedule)
        {
            rule.Id = Guid.NewGuid();
            rule.CreatedAtUtc = DateTime.UtcNow;
        }
        foreach (var trigger in copy.EventTriggers) trigger.Id = Guid.NewGuid();

        if (!TrySaveProfile(copy)) return;
        _coordinator.ProfilesEdited();
        SearchBox.Clear();
        BuildRail();
        Select(copy.Id);
    }

    private void AddLibraryWallpapers(IReadOnlyList<WallpaperAsset> assets)
    {
        if (assets.Count == 0) return;
        ShowProfiles();
        if (_selected == null)
        {
            AddProfile(template: new WallpaperProfile { Name = assets[0].Name, FolderPath = assets[0].FilePath,
                AdditionalWallpaperPaths = assets.Skip(1).Select(a => a.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SlideshowIntervalMinutes = assets.Count > 1 ? 5 : 0 });
            return;
        }
        var firstMultipleSelection = _additionalWallpapers.Count == 0 && !Directory.Exists(FolderBox.Text.Trim());
        var known = _additionalWallpapers.Select(a => a.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var source = FolderBox.Text.Trim();
        if (source.Length > 0) known.Add(source);
        foreach (var asset in assets)
        {
            if (!known.Add(asset.FilePath)) continue;
            if (FolderBox.Text.Trim().Length == 0) FolderBox.Text = asset.FilePath;
            else _additionalWallpapers.Add(asset);
        }
        if (firstMultipleSelection && _additionalWallpapers.Count > 0 && IntervalBox.Text.Trim() == "0") IntervalBox.Text = "5";
        if (_additionalWallpapers.Count > 0)
            AdditionalWallpaperCount.Text = $"{_additionalWallpapers.Count} additional {(_additionalWallpapers.Count == 1 ? "wallpaper" : "wallpapers")} · Save to keep changes";
        ValidatePath();
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e) => FolderBox.Clear();

    private void RemoveProfileWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: WallpaperAsset asset }) _additionalWallpapers.Remove(asset);
    }

    private bool TrySaveProfile(WallpaperProfile profile)
    {
        try
        {
            _store.Save(profile);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Saving profile '{profile.Name}' failed.", ex);
            System.Windows.MessageBox.Show(this,
                $"The profile could not be saved:\n{ex.Message}\n\nMake sure the app has write access to:\n{AppPaths.AppDataRoot}",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void ApplyNow_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not Guid id) return;
        if (IsDraftDirty && !SaveDraft()) return;
        _coordinator.SwitchManually(id);
        RefreshActiveGlow(); ValidatePath();
        ProfileMessage.Text = "Applied to desktop";
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDraft();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not Guid id || _selected == null)
        {
            return;
        }
        var answer = System.Windows.MessageBox.Show(this, $"Delete profile '{_selected.Name}'?", "Confirm delete",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            _store.Delete(id);
        }
        catch (Exception ex)
        {
            Logger.Error($"Deleting profile '{_selected.Name}' failed.", ex);
            System.Windows.MessageBox.Show(this, $"The profile could not be deleted:\n{ex.Message}",
                "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Logger.Info($"Deleted profile '{_selected.Name}'.");
        _loadingDraft = true;
        _selected = null;
        _selectedId = null;
        _additionalWallpapers.Clear();
        _coordinator.ProfilesEdited();
        BuildRail();
        if (_railEntries.Count > 0)
        {
            Select(_railEntries[0].Id);
        }
        else
        {
            _pathDebounce.Stop();
            ++_pathVersion;
            NameBox.Text = "";
            FolderBox.Text = "";
            ScheduleSummary.Text = "";
            TriggerSummary.Text = "";
            SetPathStatus("TextTertiaryBrush", "No location set");
            PreviewMon.ScreenSource = null;
            PreviewMon.IsVideo = false;
        }
    }

    private void AddProfile(WallpaperAsset? asset = null, WallpaperProfile? template = null)
    {
        var model = template ?? new WallpaperProfile { Name = asset?.Name ?? "", FolderPath = asset?.FilePath ?? "" };
        var editor = new ProfileEditorWindow(model, isNew: true, libraryStore: _libraryStore) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result != null)
        {
            if (!TrySaveProfile(editor.Result))
            {
                return;
            }
            _coordinator.ProfilesEdited();
            SearchBox.Clear();
            BuildRail();
            Select(editor.Result.Id);
        }
    }

    private void EditRules_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var draft = DraftGuard.Clone(_selected);
        if (!CollectFields(draft)) return;
        var editor = new ProfileEditorWindow(draft, isNew: false, libraryStore: _libraryStore) { Owner = this };
        ((System.Windows.Controls.TabControl)editor.FindName("EditorTabs")).SelectedIndex =
            ((sender as FrameworkElement)?.Tag as string) switch { "Mood" => 3, "Rules" => 2, "Playback" => 1, _ => 0 };
        if (editor.ShowDialog() == true && editor.Result != null && TrySaveProfile(editor.Result))
        {
            _coordinator.ProfilesEdited();
            _savedDraft = DraftSnapshot(); BuildRail(); SelectProfile(editor.Result.Id, true);
            ProfileMessage.Text = "Changes saved";
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsDialog != null) { _settingsDialog.Close(); return; }
        var content = SettingsPopup.Child;
        SettingsPopup.Child = null;
        var dialog = new Window { Title = "Belie settings", Owner = this, Width = 400,
            SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(500, ActualHeight - 60),
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Icon = Icon, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        dialog.SetResourceReference(Window.BackgroundProperty, "BgBrush");
        dialog.SetResourceReference(Window.ForegroundProperty, "TextPrimaryBrush");
        dialog.Closed += (_, _) => { ((ScrollViewer)dialog.Content).Content = null; SettingsPopup.Child = content; _settingsDialog = null; };
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
