using System.Collections.ObjectModel;
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
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Persistence;
using Brush = System.Windows.Media.Brush;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using TextBox = System.Windows.Controls.TextBox;

namespace WallpaperProfiles.UI;

internal partial class ProfileEditorWindow : Window
{
    private readonly WallpaperProfile _model;
    private readonly LibraryStore _libraryStore;
    private readonly ObservableCollection<WallpaperAsset> _additionalWallpapers = new();
    private readonly ObservableCollection<ScheduleRuleVm> _rules = new();
    private readonly ObservableCollection<TriggerVm> _triggers = new();

    private readonly DispatcherTimer _pathDebounce;
    private int _pathVersion;
    private bool _closed;

    public WallpaperProfile? Result { get; private set; }

    public ProfileEditorWindow(WallpaperProfile model, bool isNew, LibraryStore? libraryStore = null)
    {
        InitializeComponent();
        _model = model;
        _libraryStore = libraryStore ?? new LibraryStore(Path.Combine(AppPaths.BaseDir, "library.json"));
        TitleText.Text = isNew ? "New Profile" : $"Edit Profile – {model.Name}";
        TrySetAppIcon();

        NameBox.Text = model.Name;
        FolderBox.Text = model.FolderPath;
        FitCombo.SelectedItem = model.FitMode;
        IntervalBox.Text = model.SlideshowIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        RandomOrderCheck.IsChecked = model.SlideshowRandom;
        VideoMuteCheck.IsChecked = model.VideoMuted;
        IconSafeCheck.IsChecked = model.IconFriendlyLive;
        SceneAccentBox.Text = model.SceneAccent;
        AudioPathBox.Text = model.AmbientAudioPath;
        AmbientVolumeSlider.Value = model.AmbientVolume;
        AmbientMuteBox.IsChecked = model.AmbientMuted;
        PresetPanel.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
        PresetCombo.ItemsSource = ScenePresets.Names;

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
            AdditionalWallpaperCount.Text = $"{_additionalWallpapers.Count} additional wallpaper(s)";
            ++_pathVersion;
            _pathDebounce.Stop();
            _pathDebounce.Start();
        };
        foreach (var path in model.AdditionalWallpaperPaths)
            _additionalWallpapers.Add(new WallpaperAsset { FilePath = path, Name = Path.GetFileNameWithoutExtension(path) });

        foreach (var rule in model.Schedule)
        {
            _rules.Add(new ScheduleRuleVm(rule));
        }
        foreach (var trigger in model.EventTriggers)
        {
            _triggers.Add(new TriggerVm(trigger));
        }
        RulesList.ItemsSource = _rules;
        TriggersList.ItemsSource = _triggers;

        RefreshHints();
        Loaded += (_, _) => ValidatePath();
        Closed += (_, _) =>
        {
            _closed = true;
            ++_pathVersion;
            _pathDebounce.Stop();
        };
    }

    // ============ Path validation & preview ============

    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not string name) return;
        var preset = ScenePresets.Create(name);
        NameBox.Text = preset.Name;
        SceneAccentBox.Text = preset.SceneAccent;
        AmbientVolumeSlider.Value = preset.AmbientVolume;
        AmbientMuteBox.IsChecked = preset.AmbientMuted;
    }

    private void PickAudio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose ambient audio", Filter = "Audio|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac|All files|*.*" };
        if (dialog.ShowDialog(this) == true) AudioPathBox.Text = dialog.FileName;
    }

    private sealed record ColorDialogOwner(IntPtr Handle) : System.Windows.Forms.IWin32Window;

    private void ChooseAccent_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (SceneController.TryParseAccent(SceneAccentBox.Text, out var color))
            dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        if (dialog.ShowDialog(new ColorDialogOwner(new WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK)
            SceneAccentBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private async void ValidatePath()
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        try { await Dispatcher.InvokeAsync(ValidatePathAsync).Task.Unwrap(); }
        catch (TaskCanceledException) when (_closed || Dispatcher.HasShutdownStarted) { }
    }

    private async Task ValidatePathAsync()
    {
        if (_closed) return;
        var path = FolderBox.Text.Trim();
        var version = ++_pathVersion;

        if (_additionalWallpapers.Count > 0)
        {
            var profile = new WallpaperProfile { FolderPath = path,
                AdditionalWallpaperPaths = _additionalWallpapers.Select(a => a.FilePath).ToList() };
            var media = await Task.Run(() => WallpaperEngine.GetProfileMedia(profile));
            if (_closed || version != _pathVersion) return;
            SetPathStatus(media.Count > 0 ? "GoodBrush" : "DangerBrush",
                media.Count > 0 ? $"{media.Count} wallpapers in this profile" : "No available wallpapers");
            var first = media.FirstOrDefault();
            var thumbnail = first == null ? null : await Task.Run(() => ThumbnailLoader.Load(first, 640));
            if (_closed || version != _pathVersion) return;
            EditorPreview.ScreenSource = thumbnail;
            EditorPreview.IsVideo = first != null && WallpaperEngine.IsVideoFile(first);
            return;
        }

        if (path.Length == 0)
        {
            SetPathStatus("TextTertiaryBrush", "No location set");
            EditorPreview.ScreenSource = null;
            return;
        }

        var info = await Task.Run(() => PathInspector.Inspect(path));
        if (_closed || version != _pathVersion)
        {
            return;
        }

        if (!info.Exists)
        {
            SetPathStatus("DangerBrush", "Path not found");
            EditorPreview.ScreenSource = null;
            EditorPreview.IsVideo = false;
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
            : await Task.Run(() => ThumbnailLoader.Load(info.FirstMedia, 640));
        if (_closed || version != _pathVersion)
        {
            return;
        }
        var previous = EditorPreview.ScreenSource;
        EditorPreview.ScreenSource = image;
        EditorPreview.IsVideo = info.IsFile && info.VideoCount > 0;
        if (image != null && !ReferenceEquals(previous, image))
        {
            EditorPreview.Pulse();
        }
    }

    private void SetPathStatus(string brushKey, string text)
    {
        PathStatusDot.Fill = (Brush)FindResource(brushKey);
        PathStatusText.Text = text;
    }

    // ============ Pickers & drag-drop ============

    private void AddFromLibrary_Click(object sender, RoutedEventArgs e)
    {
        var library = new LibraryWindow(_libraryStore,
            new[] { FolderBox.Text.Trim() }.Concat(_additionalWallpapers.Select(a => a.FilePath))) { Owner = this };
        library.SetTargetProfile(string.IsNullOrWhiteSpace(NameBox.Text) ? "this profile" : NameBox.Text.Trim());
        if (library.ShowDialog() != true) return;
        var firstMultipleSelection = _additionalWallpapers.Count == 0 && !Directory.Exists(FolderBox.Text.Trim());
        var known = _additionalWallpapers.Select(a => a.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var source = FolderBox.Text.Trim();
        if (source.Length > 0) known.Add(source);
        foreach (var asset in library.SelectedAssets)
        {
            if (!known.Add(asset.FilePath)) continue;
            if (FolderBox.Text.Trim().Length == 0) FolderBox.Text = asset.FilePath;
            else _additionalWallpapers.Add(asset);
        }
        if (firstMultipleSelection && _additionalWallpapers.Count > 0 && IntervalBox.Text.Trim() == "0") IntervalBox.Text = "5";
        ValidatePath();
    }

    private void RemoveProfileWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: WallpaperAsset asset }) _additionalWallpapers.Remove(asset);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var current = FolderBox.Text.Trim();
        var start = current.Length > 0 ? current : _model.FolderPath;
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

    private void FolderBox_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (DropHelper.CanAccept(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            FolderBox.BorderBrush = (Brush)FindResource("AccentBrush");
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void FolderBox_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        FolderBox.ClearValue(TextBox.BorderBrushProperty);
    }

    private void FolderBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        FolderBox.ClearValue(TextBox.BorderBrushProperty);
        var path = DropHelper.ExtractPath(e.Data);
        if (path != null)
        {
            FolderBox.Text = path;
        }
    }

    // ============ Rules & triggers ============

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        _rules.Add(new ScheduleRuleVm(new ScheduleRule
        {
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
        }));
        RefreshHints();
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ScheduleRuleVm vm)
        {
            _rules.Remove(vm);
        }
        RefreshHints();
    }

    private void AddTrigger_Click(object sender, RoutedEventArgs e)
    {
        _triggers.Add(new TriggerVm(new EventTrigger { Priority = 5 }));
        RefreshHints();
    }

    private void RemoveTrigger_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TriggerVm vm)
        {
            _triggers.Remove(vm);
        }
        RefreshHints();
    }

    private void RefreshHints()
    {
        NoRulesHint.Text = _rules.Count == 0
            ? "No schedules"
            : "";
        NoTriggersHint.Text = _triggers.Count == 0 ? "No triggers" : "";
    }

    private void TrySetAppIcon()
    {
        UiAppearance.Attach(this);
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            Warn("Please enter a profile name.");
            return;
        }

        var folder = FolderBox.Text.Trim();
        var accent = SceneAccentBox.Text.Trim();
        if (accent.Length > 0 && !SceneController.TryParseAccent(accent, out _))
        {
            Warn("Enter a six-digit accent color such as #B7CDBC, or leave it blank for the default.");
            return;
        }
        if (folder.Length == 0 && _additionalWallpapers.Count == 0)
        {
            Warn("Please choose a wallpaper image, video, or folder.");
            return;
        }
        if (folder.Length > 0 && !Directory.Exists(folder) && !File.Exists(folder))
        {
            var reply = System.Windows.MessageBox.Show(this, $"The image, video, or folder does not exist:\n{folder}\n\nSave anyway?",
                "Missing location", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (reply != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (!int.TryParse(IntervalBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval)
            || interval < 0 || interval > 1440)
        {
            Warn("Slideshow interval must be a whole number between 0 and 1440 minutes.");
            return;
        }

        var rules = new List<ScheduleRule>();
        foreach (var vm in _rules)
        {
            if (!TimeOnly.TryParseExact(vm.StartTimeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !TimeOnly.TryParseExact(vm.EndTimeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                Warn("A schedule rule has invalid times (use HH:mm, e.g. 09:00).");
                return;
            }
            if (start == end)
            {
                Warn("A schedule rule's start and end time are equal, so it would never match. Pick two different times.");
                return;
            }
            var days = new HashSet<DayOfWeek>();
            for (var i = 0; i < 7; i++)
            {
                if (vm.Days[i])
                {
                    days.Add((DayOfWeek)i);
                }
            }
            if (days.Count == 0)
            {
                Warn("A schedule rule has no days selected.");
                return;
            }
            vm.Model.StartTime = start;
            vm.Model.EndTime = end;
            vm.Model.DaysOfWeek = days;
            rules.Add(vm.Model);
        }

        var triggers = new List<EventTrigger>();
        foreach (var vm in _triggers)
        {
            var trigger = vm.Model;
            trigger.Type = vm.Type;
            trigger.Condition = vm.Condition.Trim();
            trigger.Priority = int.TryParse(vm.PriorityText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var prio)
                ? Math.Clamp(prio, 1, 99)
                : 5;

            switch (trigger.Type)
            {
                case TriggerType.BelowBatteryPercent:
                case TriggerType.IdleForMinutes:
                    if (!double.TryParse(trigger.Condition, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
                    {
                        Warn($"The '{trigger.Type}' trigger needs a positive number in the 'when' box.");
                        return;
                    }
                    break;
                case TriggerType.ProcessLaunch:
                    if (trigger.Condition.Length == 0)
                    {
                        Warn("The process-launch trigger needs a program name, e.g. notepad.exe");
                        return;
                    }
                    break;
            }
            triggers.Add(trigger);
        }

        _model.Name = name;
        _model.FolderPath = folder;
        _model.AdditionalWallpaperPaths = _additionalWallpapers.Select(a => a.FilePath)
            .Where(p => !string.Equals(p, folder, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _model.FitMode = (FitMode)(FitCombo.SelectedItem ?? FitMode.Fill);
        _model.SlideshowIntervalMinutes = interval;
        _model.SlideshowRandom = RandomOrderCheck.IsChecked == true;
        _model.VideoMuted = VideoMuteCheck.IsChecked != false;
        _model.IconFriendlyLive = IconSafeCheck.IsChecked == true;
        _model.SceneAccent = accent.ToUpperInvariant();
        _model.AmbientAudioPath = AudioPathBox.Text.Trim();
        _model.AmbientVolume = (int)Math.Round(AmbientVolumeSlider.Value);
        _model.AmbientMuted = AmbientMuteBox.IsChecked == true;
        _model.Schedule = rules;
        _model.EventTriggers = triggers;

        Result = _model;
        DialogResult = true;
    }

    private void Warn(string message) =>
        System.Windows.MessageBox.Show(this, message, "Cannot save", MessageBoxButton.OK, MessageBoxImage.Warning);
}
