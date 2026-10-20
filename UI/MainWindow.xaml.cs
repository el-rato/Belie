using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperProfiles.Autostart;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;

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
    private List<string> _wallpaperOrder = new();

    private readonly DispatcherTimer _pathDebounce;
    private readonly DispatcherTimer _dashboardTimer;
    private int _pathVersion;

    private sealed record OverrideDuration(string Label, TimeSpan Duration);

    public MainWindow(WallpaperCoordinator coordinator, ProfileStore store, SettingsStore settingsStore, LibraryStore? libraryStore = null, ProfileShortcutManager? profileShortcuts = null)
    {
        InitializeComponent();
        InstalledVersionText.Text = $"Belie {AppUpdater.CurrentVersion.ToString(3)}";
        if (System.Windows.Application.Current is App app)
        {
            app.Updates.Changed += RefreshUpdateStatus;
            Closed += (_, _) => app.Updates.Changed -= RefreshUpdateStatus;
            RefreshUpdateStatus();
        }

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
        _additionalWallpapers.CollectionChanged += (_, _) =>
        {
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

}
