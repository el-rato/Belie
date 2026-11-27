using System.Globalization;
using System.Windows;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
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
        if (!TryCollectRules(out var rules, out var triggers)) return false;

        profile.Name = name;
        profile.FolderPath = FolderBox.Text.Trim();
        profile.WallpaperOrder = _wallpaperOrder.ToList();
        profile.AdditionalWallpaperPaths = _additionalWallpapers.Select(a => a.FilePath)
            .Where(path => !path.Equals(profile.FolderPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        profile.FitMode = (FitMode)(FitCombo.SelectedItem ?? FitMode.Fill);
        profile.SlideshowIntervalMinutes = interval;
        profile.SlideshowRandom = RandomBox.IsChecked == true;
        profile.VideoMuted = VideoMuteBox.IsChecked != false;
        profile.IconFriendlyLive = IconSafeBox.IsChecked == true;
        profile.Schedule = rules;
        profile.EventTriggers = triggers;
        return true;
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || !TryLeaveDraft()) return;

        var copy = DraftGuard.Clone(_selected);
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
            LoadRules(null);
            SetPathStatus("TextTertiaryBrush", "No location set");
            PreviewMon.ScreenSource = null;
            PreviewMon.IsVideo = false;
        }
    }

    /// <summary>
    /// Makes the profile straight away and opens it on the Profiles page, name ready to type over,
    /// so there is no separate window to fill in first.
    /// </summary>
    private void AddProfile(WallpaperAsset? asset = null, WallpaperProfile? template = null)
    {
        var profile = template ?? new WallpaperProfile { Name = asset?.Name ?? "", FolderPath = asset?.FilePath ?? "" };
        var baseName = string.IsNullOrWhiteSpace(profile.Name) ? "New profile" : profile.Name.Trim();
        profile.Name = baseName;
        for (var suffix = 2; _coordinator.Profiles.Any(p => string.Equals(p.Name, profile.Name, StringComparison.CurrentCultureIgnoreCase)); suffix++)
            profile.Name = $"{baseName} {suffix}";
        if (!TrySaveProfile(profile)) return;
        _coordinator.ProfilesEdited();
        SearchBox.Clear();
        BuildRail();
        Select(profile.Id);
        ProfileTabs.SelectedIndex = 0;
        ProfileMessage.Text = string.IsNullOrWhiteSpace(profile.FolderPath) && profile.AdditionalWallpaperPaths.Count == 0
            ? "Name it and pick a wallpaper" : "Created";
        Dispatcher.BeginInvoke(() => { NameBox.Focus(); NameBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }
}
