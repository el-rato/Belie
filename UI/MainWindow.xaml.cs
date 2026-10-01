using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    private readonly WallpaperCoordinator _coordinator;
    private readonly ProfileStore _store;
    private readonly SettingsStore _settingsStore;
    private readonly LibraryStore _libraryStore;
    private AppSettings _settings;

    private readonly List<RailEntry> _railEntries = new();
    private Guid? _selectedId;
    private WallpaperProfile? _selected;
    private LibraryWindow? _libraryView;

    private readonly DispatcherTimer _pathDebounce;
    private readonly DispatcherTimer _dashboardTimer;
    private int _pathVersion;

    private sealed record OverrideDuration(string Label, TimeSpan Duration);

    public MainWindow(WallpaperCoordinator coordinator, ProfileStore store, SettingsStore settingsStore, LibraryStore? libraryStore = null)
    {
        InitializeComponent();
        ((System.Windows.Controls.Image)PreviewMon.FindName("ScreenImage")).Stretch = Stretch.Uniform;
        _coordinator = coordinator;
        _store = store;
        _settingsStore = settingsStore;
        _libraryStore = libraryStore ?? new LibraryStore(Path.Combine(AppPaths.BaseDir, "library.json"));
        _settings = settingsStore.Load();
        TrySetAppIcon();

        _pathDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _pathDebounce.Tick += (_, _) =>
        {
            _pathDebounce.Stop();
            ValidatePath();
        };
        FolderBox.TextChanged += (_, _) =>
        {
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
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        AutostartToggle.IsChecked = AutostartManager.IsEnabled();
        MinimizedToggle.IsChecked = _settings.StartMinimizedToTray;
        BuildRail();
        if (_railEntries.Count > 0)
        {
            Select(_railEntries[0].Id);
        }
        ShowDashboard();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void TrySetAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (exeIcon != null)
                {
                    Icon = Imaging.CreateBitmapSourceFromHIcon(
                        exeIcon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
        }
        catch
        {
        }
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

    private System.Windows.Media.Brush ThemeBrush(string key) => (System.Windows.Media.Brush)FindResource(key);

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        ShowProfiles();
        AddProfile();
    }

    private void ShowProfiles_Click(object sender, RoutedEventArgs e) => ShowProfiles();

    private void ShowProfiles()
    {
        CanvasPane.Visibility = Visibility.Collapsed;
        CanvasNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        ProfilePane.Visibility = Visibility.Visible;
        LibraryPane.Visibility = Visibility.Collapsed;
        DashboardPane.Visibility = Visibility.Collapsed;
        ProfilesNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
        LibraryNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        DashboardNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        WorkspaceTitle.Text = "/  Wallpaper profiles";
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        CanvasPane.Visibility = Visibility.Collapsed;
        CanvasNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        try
        {
            if (_libraryView == null)
            {
                var sources = _libraryStore.Exists ? Array.Empty<string>() : _coordinator.Profiles.Select(p => p.FolderPath).ToArray();
                var view = new LibraryWindow(_libraryStore, sources);
                LibraryPane.Content = view.CreateInlineContent(asset =>
                {
                    ShowProfiles();
                    if (_selected != null) FolderBox.Text = asset.FilePath;
                    else AddProfile(asset);
                }, ShowProfiles);
                _libraryView = view;
            }
            ProfilePane.Visibility = Visibility.Collapsed;
            DashboardPane.Visibility = Visibility.Collapsed;
            LibraryPane.Visibility = Visibility.Visible;
            LibraryNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
            ProfilesNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            DashboardNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            WorkspaceTitle.Text = "/  Wallpaper library";
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
        CanvasPane.Visibility = Visibility.Collapsed;
        CanvasNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        DashboardPane.Visibility = Visibility.Visible;
        ProfilePane.Visibility = Visibility.Collapsed;
        LibraryPane.Visibility = Visibility.Collapsed;
        DashboardNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
        ProfilesNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        LibraryNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        WorkspaceTitle.Text = "/  Activity";
        RefreshDashboard();
    }

    private void ShowCanvas_Click(object sender, RoutedEventArgs e)
    {
        CanvasPane.Content ??= new DesktopCanvasView();
        CanvasPane.Visibility = Visibility.Visible;
        ProfilePane.Visibility = Visibility.Collapsed;
        LibraryPane.Visibility = Visibility.Collapsed;
        DashboardPane.Visibility = Visibility.Collapsed;
        CanvasNavButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSubtleBrush");
        ProfilesNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        LibraryNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        DashboardNavButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        WorkspaceTitle.Text = "/  Desktop canvas";
    }

    internal void OpenCanvas() => Dispatcher.BeginInvoke(DispatcherPriority.Background,
        new Action(() => ShowCanvas_Click(this, new RoutedEventArgs())));

    private void CoordinatorStateChanged()
    {
        RefreshActiveGlow();
        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        var snapshot = _coordinator.GetActivitySnapshot();
        var now = _coordinator.LocalNow;
        ActiveProfileText.Text = snapshot.ActiveProfile?.Name ?? "No profile active";
        ActivationReasonText.Text = snapshot.Reason;
        AmbientStatusText.Text = _coordinator.AmbientStatus;
        AmbientMuteButton.IsEnabled = _coordinator.HasAmbientAudio;
        AmbientMuteButton.Content = _coordinator.AmbientIsMuted ? "Unmute ambient" : "Mute ambient";
        AutomationModeText.Text = snapshot.Paused ? "Paused" : "Running";
        AutomationHintText.Text = snapshot.Paused ? "Automatic changes are paused. Manual choices still work."
            : "Schedules and event triggers are enabled.";
        PauseAutomationButton.Content = snapshot.Paused ? "Resume automation" : "Pause automation";
        NextScheduleText.Text = snapshot.NextScheduleAtLocal is { } next ? FormatWhen(next, now) : "No schedules";
        NextScheduleHint.Text = snapshot.NextScheduleAtLocal == null ? "Add a schedule to a profile to automate your day."
            : (snapshot.NextScheduledProfile != null ? snapshot.NextScheduledProfile + " is scheduled at this time. " : "No profile is scheduled at this time. ")
              + "Overrides and events can take priority.";

        if (!ReferenceEquals(OverrideProfileCombo.ItemsSource, _coordinator.Profiles))
        {
            var id = OverrideProfileCombo.SelectedValue as Guid? ?? _coordinator.ActiveProfileId ?? _selectedId;
            OverrideProfileCombo.ItemsSource = _coordinator.Profiles;
            OverrideProfileCombo.SelectedItem = _coordinator.Profiles.FirstOrDefault(p => p.Id == id) ?? _coordinator.Profiles.FirstOrDefault();
        }
        OverrideProfileCombo.IsEnabled = _coordinator.Profiles.Count > 0;
        StartOverrideButton.IsEnabled = OverrideProfileCombo.SelectedItem is WallpaperProfile;
        ResumeAutomaticButton.IsEnabled = snapshot.Paused || snapshot.ManualOverride != null;
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
        _railEntries.Clear();

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
                Width = 188,
                BezelWidth = 64,
                BezelHeight = 44,
                ShowName = true,
                ProfileName = profile.Name,
                HasRules = profile.Schedule.Count > 0 || profile.EventTriggers.Count > 0,
                IsActive = profile.Id == _coordinator.ActiveProfileId,
            };

            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Style = (Style)FindResource("ProfileCard"),
                Child = monitor,
            };
            card.MouseLeftButtonUp += (_, _) => Select(profile.Id);
            RailStack.Children.Add(card);
            _railEntries.Add(new RailEntry(profile.Id, card, monitor));

            _ = LoadRailThumbAsync(profile, monitor);
        }

        FilterRail();
    }

    private static async Task LoadRailThumbAsync(WallpaperProfile profile, MonitorThumbnail monitor)
    {
        var isLive = WallpaperEngine.IsLiveFile(profile.FolderPath);
        // Live sources (video/GIF) load through the video frame grabber / image decoder directly.
        var image = await Task.Run(() => ThumbnailLoader.Load(
            isLive ? profile.FolderPath : null, 320)
            ?? ThumbnailLoader.FirstImage(profile.FolderPath, 320));
        monitor.IsVideo = isLive;
        monitor.ScreenSource = image;
    }

    private void Select(Guid id)
    {
        _selectedId = id;
        _selected = _coordinator.Profiles.FirstOrDefault(p => p.Id == id);
        if (_selected == null)
        {
            return;
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
        FolderBox.Text = _selected.FolderPath;
        FitCombo.SelectedItem = _selected.FitMode;
        IntervalBox.Text = _selected.SlideshowIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        RandomBox.IsChecked = _selected.SlideshowRandom;
        VideoMuteBox.IsChecked = _selected.VideoMuted;
        IconSafeBox.IsChecked = _selected.IconFriendlyLive;
        ScheduleSummary.Text = _selected.Schedule.Count > 0
            ? $"Schedule: {_selected.Schedule.Count} rule(s)" : "Schedule: none";
        TriggerSummary.Text = _selected.EventTriggers.Count > 0
            ? $"Triggers: {string.Join(", ", _selected.EventTriggers.Select(t => t.Describe()))}" : "Triggers: none";
        SceneSummary.Text = (_selected.SceneAccent.Length > 0 ? "Scene accent: " + _selected.SceneAccent : "Default accent")
            + (_selected.AmbientAudioPath.Length > 0 ? " · ambient: " + Path.GetFileNameWithoutExtension(_selected.AmbientAudioPath) : " · no ambient track");
    }

    private void RefreshActiveGlow()
    {
        foreach (var entry in _railEntries)
        {
            var profile = _coordinator.Profiles.FirstOrDefault(p => p.Id == entry.Id);
            entry.Monitor.IsActive = profile != null && profile.Id == _coordinator.ActiveProfileId;
        }
    }

    // ============ Path validation & preview ============

    private async void ValidatePath()
    {
        var path = FolderBox.Text.Trim();
        var version = ++_pathVersion;

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
        PreviewMon.ScreenSource = image;
        PreviewMon.IsVideo = info.IsFile && info.VideoCount > 0;
        if (image != null && !ReferenceEquals(previous, image))
        {
            PreviewMon.Pulse();
        }
    }

    private static (bool Exists, bool IsFile, int ImageCount, int VideoCount, string? FirstMedia) InspectPath(string path)
        => PathInspector.Inspect(path);

    private void SetPathStatus(string brushKey, string text)
    {
        PathStatusDot.Fill = (Brush)FindResource(brushKey);
        PathStatusText.Text = text;
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
            System.Windows.MessageBox.Show(this, "Please enter a profile name.", "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (!int.TryParse(IntervalBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval)
            || interval < 0 || interval > 1440)
        {
            System.Windows.MessageBox.Show(this, "Slideshow interval must be a whole number between 0 and 1440.",
                "Cannot save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        profile.Name = name;
        profile.FolderPath = FolderBox.Text.Trim();
        profile.FitMode = (FitMode)(FitCombo.SelectedItem ?? FitMode.Fill);
        profile.SlideshowIntervalMinutes = interval;
        profile.SlideshowRandom = RandomBox.IsChecked == true;
        profile.VideoMuted = VideoMuteBox.IsChecked != false;
        profile.IconFriendlyLive = IconSafeBox.IsChecked == true;
        return true;
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;

        var copy = System.Text.Json.JsonSerializer.Deserialize<WallpaperProfile>(
            System.Text.Json.JsonSerializer.Serialize(_selected))!;
        if (!CollectFields(copy)) return;

        copy.Id = Guid.NewGuid();
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
        if (_selectedId is not Guid id || !CollectFields())
        {
            return;
        }
        // Persist before applying so the running wallpaper always matches what is on disk.
        if (!TrySaveProfile(_selected!))
        {
            return;
        }
        _coordinator.ProfilesEdited();
        _coordinator.SwitchManually(id);
        RefreshActiveGlow();
        ValidatePath();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || !CollectFields())
        {
            return;
        }
        if (!TrySaveProfile(_selected))
        {
            return;
        }
        _coordinator.ProfilesEdited();
        BuildRail();
        Select(_selected.Id);
    }

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
        _selected = null;
        _selectedId = null;
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
        var editor = new ProfileEditorWindow(model, isNew: true) { Owner = this };
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
        if (_selected == null)
        {
            return;
        }
        var editor = new ProfileEditorWindow(_selected, isNew: false) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            if (!TrySaveProfile(_selected))
            {
                return;
            }
            _coordinator.ProfilesEdited();
            BuildRail();
            Select(_selected.Id);
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
