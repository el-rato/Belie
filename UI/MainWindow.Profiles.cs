using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.UI;

internal partial class MainWindow
{
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
        _wallpaperOrder = new List<string>(_selected.WallpaperOrder ?? new());
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
        LoadRules(_selected);
        _loadingDraft = false;
        _savedDraft = DraftSnapshot();
        ProfileMessage.Text = _selected.Id == _coordinator.ActiveProfileId ? "Applied" : "Not applied";
        UpdateDraftState(); UpdatePreviewFit();
    }

    private string DraftSnapshot() => DraftGuard.Snapshot(new { NameBox.Text, Path = FolderBox.Text,
        Interval = IntervalBox.Text, Fit = FitCombo.SelectedItem, Random = RandomBox.IsChecked,
        Muted = VideoMuteBox.IsChecked, Icons = IconSafeBox.IsChecked,
        Additional = _additionalWallpapers.Select(a => a.FilePath).ToArray(), Order = _wallpaperOrder.ToArray(), Rules = RulesSnapshot() });
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
            ProfileMessage.Text = _selected?.Id == _coordinator.ActiveProfileId ? "Applied" : "Not applied";
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
        ProfileMessage.Text = "Changes saved"; ShowToast("Saved " + draft.Name);
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
        UpdateShellLayout();
        var fit = FitCombo.SelectedItem is FitMode selected ? selected : FitMode.Fill;
        var screen = fit == FitMode.Span ? System.Windows.Forms.SystemInformation.VirtualScreen : System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var ratio = screen.Width / (double)screen.Height;
        var height = Math.Min(ActualHeight < 720 ? 240d : 360d, Math.Max(200, ProfilePane.ActualWidth - 50) / ratio);
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
            ?? _profileShortcuts?.GetNote(_selected.Id) ?? "Switch to this profile from anywhere, including the tray";
    }

    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _profileShortcuts == null) return;
        _shortcutProfileId = _selected.Id;
        _pendingShortcut = _selected.KeyboardShortcut;
        ShortcutCapture.Text = _pendingShortcut.Replace("+", " + ");
        ShortcutMessage.Text = _profileShortcuts.GetError(_selected.Id) ?? _profileShortcuts.GetNote(_selected.Id) ?? "Works while Belie is in the tray.";
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
}
