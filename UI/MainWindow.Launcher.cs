using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WallpaperProfiles.Persistence;

namespace WallpaperProfiles.UI;

/// <summary>One thing the launcher can do. Keys is only a hint shown beside it.</summary>
internal sealed record LauncherCommand(string Title, string Group, string Glyph, Action Run, string Keys = "", string Keywords = "");

/// <summary>Forgiving search: every word typed must match the title, group or keywords somewhere.</summary>
internal static class LauncherSearch
{
    public static int Score(string query, LauncherCommand command)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;
        var total = 0;
        foreach (var word in words)
        {
            var best = Math.Max(Match(word, command.Title) * 3, Math.Max(Match(word, command.Keywords), Match(word, command.Group)) * 2);
            if (best == 0) return -1;
            total += best;
        }
        return total;
    }

    public static IReadOnlyList<LauncherCommand> Filter(string query, IEnumerable<LauncherCommand> commands)
    {
        if (string.IsNullOrWhiteSpace(query)) return commands.ToList();
        return commands.Select((command, index) => (command, index, score: Score(query, command)))
            .Where(entry => entry.score > 0)
            .OrderByDescending(entry => entry.score).ThenBy(entry => entry.index)
            .Select(entry => entry.command).ToList();
    }

    /// <summary>Starts the text: 4. Starts a word: 3. Anywhere: 2. Letters in order: 1. Not at all: 0.</summary>
    private static int Match(string word, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (at == 0) return 4;
        if (at > 0)
        {
            for (var i = at; i >= 0; i = i + 1 < text.Length ? text.IndexOf(word, i + 1, StringComparison.OrdinalIgnoreCase) : -1)
                if (!char.IsLetterOrDigit(text[i - 1])) return 3;
            return 2;
        }
        var next = 0;
        foreach (var letter in text)
            if (next < word.Length && char.ToLowerInvariant(letter) == char.ToLowerInvariant(word[next])) next++;
        return next == word.Length && word.Length > 1 ? 1 : 0;
    }
}

internal partial class MainWindow
{
    private List<LauncherCommand> _launcherCommands = new();
    private IInputElement? _launcherReturnFocus;
    private List<KeyGesture>? _launcherGestures;

    internal bool LauncherOpen => LauncherLayer.Visibility == Visibility.Visible;

    private IReadOnlyList<KeyGesture> LauncherGestures => _launcherGestures ??= ShellStore.Default.Load().LauncherKeys
        .Select(text => { try { return new KeyGestureConverter().ConvertFromInvariantString(text) as KeyGesture; } catch { return null; } })
        .OfType<KeyGesture>().ToList();

    internal void OpenLauncher()
    {
        if (LauncherOpen) return;
        MorePopup.IsOpen = false;
        _launcherReturnFocus = Keyboard.FocusedElement;
        _launcherCommands = BuildLauncherCommands();
        LauncherBox.Text = "";
        RefreshLauncher();
        LauncherLayer.Visibility = Visibility.Visible;
        if (SystemParameters.ClientAreaAnimation)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var time = TimeSpan.FromMilliseconds(150);
            LauncherScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, time) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            LauncherScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, time) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            LauncherLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop });
        }
        LauncherBox.Focus();
        Keyboard.Focus(LauncherBox);
    }

    internal void CloseLauncher()
    {
        if (!LauncherOpen) return;
        LauncherLayer.Visibility = Visibility.Collapsed;
        LauncherList.ItemsSource = null;
        _launcherCommands.Clear();
        if (_launcherReturnFocus is UIElement { IsVisible: true } element) element.Focus();
        _launcherReturnFocus = null;
    }

    private void RefreshLauncher()
    {
        var results = LauncherSearch.Filter(LauncherBox.Text, _launcherCommands);
        LauncherList.ItemsSource = results;
        LauncherList.SelectedIndex = results.Count > 0 ? 0 : -1;
        LauncherEmpty.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LauncherHint.Visibility = LauncherBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Count > 0) LauncherList.ScrollIntoView(results[0]);
    }

    private void RunLauncherCommand(LauncherCommand? command)
    {
        if (command == null) return;
        CloseLauncher();
        try { command.Run(); }
        catch (Exception ex)
        {
            Infrastructure.Logger.Error("Running \"" + command.Title + "\" from the launcher failed.", ex);
            ShowToast("Could not " + command.Title.ToLowerInvariant());
        }
    }

    private void LauncherBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshLauncher();

    private void LauncherBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var count = LauncherList.Items.Count;
        switch (e.Key)
        {
            case Key.Escape: CloseLauncher(); break;
            case Key.Enter: RunLauncherCommand(LauncherList.SelectedItem as LauncherCommand); break;
            case Key.Down when count > 0: MoveLauncher(1); break;
            case Key.Up when count > 0: MoveLauncher(-1); break;
            case Key.PageDown when count > 0: MoveLauncher(8); break;
            case Key.PageUp when count > 0: MoveLauncher(-8); break;
            case Key.Tab: break;
            default: return;
        }
        e.Handled = true;
    }

    private void MoveLauncher(int by)
    {
        var count = LauncherList.Items.Count;
        var index = LauncherList.SelectedIndex + by;
        index = Math.Abs(by) == 1 ? (index + count) % count : Math.Clamp(index, 0, count - 1);
        LauncherList.SelectedIndex = index;
        LauncherList.ScrollIntoView(LauncherList.SelectedItem);
    }

    private void LauncherList_Click(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject) is { } source && ItemsControl.ContainerFromElement(LauncherList, source) is ListBoxItem item)
            RunLauncherCommand(item.DataContext as LauncherCommand);
    }

    private void LauncherLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == LauncherLayer) CloseLauncher();
    }

    private void Search_Click(object sender, RoutedEventArgs e) => OpenLauncher();

    /// <summary>Keys that work anywhere in the window. Recorders mark their keys handled, so they never get here.</summary>
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Handled || ShortcutPopup.IsOpen) return;
        if (LauncherGestures.Any(gesture => gesture.Matches(this, e)))
        {
            if (LauncherOpen) CloseLauncher(); else OpenLauncher();
            e.Handled = true;
            return;
        }
        if (LauncherOpen || Keyboard.Modifiers != ModifierKeys.Control) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var onProfiles = ProfilePane.Visibility == Visibility.Visible;
        if (key == Key.S && onProfiles && _selected != null) Save_Click(this, e);
        else if (key == Key.Enter && onProfiles && _selected != null) ApplyNow_Click(this, e);
        else if (key == Key.N) NewProfile_Click(this, e);
        else return;
        e.Handled = true;
    }

    private void ShowLauncherKeys()
    {
        var first = ShellStore.Default.Load().LauncherKeys.FirstOrDefault() ?? "";
        SearchPillKeys.Text = first;
        SearchPill.ToolTip = string.IsNullOrEmpty(first) ? "Search Belie" : "Search Belie (" + first + ")";
    }

    private List<LauncherCommand> BuildLauncherCommands()
    {
        var list = new List<LauncherCommand>
        {
            new("Home", "Go to", "", ShowDashboard, Keywords: "overview dashboard start"),
            new("Profiles", "Go to", "", ShowProfiles, Keywords: "edit wallpapers"),
            new("Widgets", "Go to", "", () => ShowCanvas_Click(this, new RoutedEventArgs()), Keywords: "canvas clock overlay"),
            new("Library", "Go to", "", () => OpenLibrary_Click(this, new RoutedEventArgs()), Keywords: "explore images videos browse"),
            new("Discover", "Go to", "", () => OpenDiscover_Click(this, new RoutedEventArgs()), Keywords: "wallhaven online find new download"),
            new("Top bar", "Go to", "", () => ShowTopBar_Click(this, new RoutedEventArgs()), Keywords: "status bar panel"),
            new("Workspaces", "Go to", "", () => ShowWorkspaces_Click(this, new RoutedEventArgs()), Keywords: "desktops virtual"),
            new("Transitions", "Go to", "", () => ShowTransitions_Click(this, new RoutedEventArgs()), Keywords: "fade animation"),
            new("Team", "Go to", "", () => ShowTeam_Click(this, new RoutedEventArgs()), Keywords: "share people"),
            new("Wallpaper switcher", "Go to", "", () => ShowSwitcher_Click(this, new RoutedEventArgs()), Keywords: "keys picker"),
            new("New profile", "Profiles", "", () => NewProfile_Click(this, new RoutedEventArgs()), "Ctrl+N", "add create"),
        };
        if (_selected != null)
        {
            list.Add(new("Save profile", "Profiles", "", () => { ShowProfiles(); Save_Click(this, new RoutedEventArgs()); }, "Ctrl+S", "changes " + _selected.Name));
            list.Add(new("Apply " + _selected.Name, "Profiles", "", () => ApplyNow_Click(this, new RoutedEventArgs()), "Ctrl+Enter", "desktop set use"));
        }
        foreach (var profile in _coordinator.Profiles)
        {
            var id = profile.Id;
            list.Add(new("Apply " + profile.Name, "Profile", "", () => _coordinator.SwitchManually(id), profile.KeyboardShortcut ?? "", "desktop set use switch"));
            list.Add(new("Edit " + profile.Name, "Profile", "", () => { ShowProfiles(); Select(id); }, Keywords: "change open"));
        }
        var activity = _coordinator.GetActivitySnapshot();
        list.Add(new(activity.Paused ? "Resume automation" : "Pause automation", "Automation", "", _coordinator.TogglePaused, Keywords: "schedule rules stop"));
        if (activity.ManualOverride != null)
            list.Add(new("Go back to automatic", "Automation", "", _coordinator.ResumeAutomatic, Keywords: "override resume end"));
        list.Add(new("Open the wallpaper switcher", "Wallpaper", "", () => OpenSwitcher(), Keywords: "next previous pick"));
        list.Add(new("Settings", "App", "", () => Settings_Click(this, new RoutedEventArgs()), Keywords: "preferences options"));
        list.Add(new("Check for updates", "App", "", () => CheckUpdates_Click(this, new RoutedEventArgs()), Keywords: "version upgrade"));
        list.Add(new("What's new", "App", "", () => ReleaseNotes_Click(this, new RoutedEventArgs()), Keywords: "release notes changes"));
        list.Add(new((AutostartToggle.IsChecked == true ? "Stop" : "Start") + " Belie with Windows", "App", "",
            () => AutostartToggle.IsChecked = AutostartToggle.IsChecked != true, Keywords: "startup boot login"));
        list.Add(new((MinimizedToggle.IsChecked == true ? "Open the window" : "Stay in the tray") + " when Belie starts", "App", "",
            () => MinimizedToggle.IsChecked = MinimizedToggle.IsChecked != true, Keywords: "minimized tray startup"));
        foreach (var level in Enum.GetValues<GlassLevel>())
            if (level != UiAppearance.Glass)
                list.Add(new("Glass: " + level, "Look", "", () => GlassPicker.SelectedItem = level, Keywords: "transparency blur window"));
        list.Add(new("Open themes folder", "Look", "", () => OpenThemesFolder_Click(this, new RoutedEventArgs()), Keywords: "custom theme config json colours colors make your own"));
        foreach (var theme in UiAppearance.Themes)
            if (theme != UiAppearance.Current)
                list.Add(new("Theme: " + theme.Name, "Look", "", () => ThemePicker.SelectedItem = theme, Keywords: "colour color appearance"));
        return list;
    }
}
