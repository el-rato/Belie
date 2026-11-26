using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace WallpaperProfiles.UI;

/// <summary>Counts contributions, saves the team and keeps the walker in step. Shared by the page and the tray app.</summary>
internal static class TeamService
{
    private static readonly SemaphoreSlim Counting = new(1);

    public static event Action? Changed;

    public static void Update(Action<TeamState> change)
    {
        var state = CreaturePack.LoadState();
        change(state);
        CreaturePack.SaveState(state);
        DesktopWalker.Apply(state);
        Changed?.Invoke();
    }

    /// <summary>Counts again unless <paramref name="ifOlderThan"/> has not passed since the last count.</summary>
    public static async Task RefreshAsync(TimeSpan ifOlderThan, CancellationToken token = default)
    {
        if (!await Counting.WaitAsync(0, token)) return;
        try
        {
            var state = CreaturePack.LoadState();
            if (state.CountedAt is { } at && DateTime.UtcNow - at < ifOlderThan) return;
            var count = await GitHubContributions.ReadAsync(state.GitHubUser, token);
            var streak = TeamProgress.Streak(count.Days, DateOnly.FromDateTime(DateTime.Now));
            Update(saved =>
            {
                CreaturePack.Fill(saved, CreaturePack.Installed());
                TeamProgress.Gain(saved, count.Total, streak);
                saved.Login = count.Login; saved.CountedAt = DateTime.UtcNow;
            });
        }
        finally { Counting.Release(); }
    }
}

/// <summary>The team: creatures that level up with the person's GitHub contributions.</summary>
internal sealed class TeamPage : System.Windows.Controls.UserControl
{
    private static readonly Dictionary<string, Color> TypeColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Leaf"] = Color.FromRgb(0x7C, 0xCB, 0x6F), ["Ember"] = Color.FromRgb(0xF0, 0x8A, 0x4B), ["Tide"] = Color.FromRgb(0x64, 0xAA, 0xF0),
        ["Spark"] = Color.FromRgb(0xF5, 0xD5, 0x47), ["Bug"] = Color.FromRgb(0xB5, 0xC0, 0x4A), ["Shade"] = Color.FromRgb(0x9A, 0x6E, 0xD2),
        ["Air"] = Color.FromRgb(0xB8, 0xA8, 0xF0), ["Stone"] = Color.FromRgb(0xA0, 0xA0, 0xAA)
    };

    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private CancellationTokenSource? _downloading;

    public TeamPage()
    {
        var heading = new TextBlock { Text = "Team" };
        heading.SetResourceReference(StyleProperty, "WorkspaceHeading");
        var about = new TextBlock { Text = "Creatures that level up with your GitHub contributions. A streak of days multiplies what they earn, and the leader walks on your windows.", TextWrapping = TextWrapping.Wrap };
        about.SetResourceReference(StyleProperty, "WorkspaceCaption");
        _status.SetResourceReference(StyleProperty, "HintText");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        top.Children.Add(heading); top.Children.Add(about); top.Children.Add(_status);
        var layout = new StackPanel { Margin = new Thickness(24, 20, 24, 24), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch };
        layout.Children.Add(top); layout.Children.Add(_body);
        Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Again() => Dispatcher.BeginInvoke(Show);
        Loaded += (_, _) => { TeamService.Changed += Again; Show(); _ = Count(TimeSpan.FromMinutes(30)); };
        Unloaded += (_, _) => TeamService.Changed -= Again;
    }

    private async Task Count(TimeSpan ifOlderThan)
    {
        if (CreaturePack.Installed().Count == 0) return;
        _status.Text = "Counting your contributions…";
        try { await TeamService.RefreshAsync(ifOlderThan); _status.Text = ""; }
        catch (Exception ex) { _status.Text = ex.Message; Logger.Warn("Counting contributions failed: " + ex.Message); }
    }

    private void Show()
    {
        _body.Children.Clear();
        var creatures = CreaturePack.Installed();
        var state = CreaturePack.LoadState();
        if (creatures.Count == 0) { ShowEmpty(state); return; }
        if (state.Members.Count < Math.Min(TeamProgress.Size, creatures.Count)) { CreaturePack.Fill(state, creatures); CreaturePack.SaveState(state); }

        var card = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(28, 22, 28, 18) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
        var inside = new StackPanel();
        card.Child = inside;

        var title = new DockPanel();
        var refresh = Pill("Refresh", false, () => _ = Count(TimeSpan.Zero));
        DockPanel.SetDock(refresh, Dock.Right);
        title.Children.Add(refresh);
        title.Children.Add(new TextBlock { Text = "Team", FontSize = 22, VerticalAlignment = VerticalAlignment.Center });
        inside.Children.Add(title);
        var trainer = state.LastTotal < 0 ? "Not counted yet"
            : $"{(state.Login.Length > 0 ? state.Login : "Trainer")}  ·  {state.LastTotal} contributions  ·  streak {state.Streak} ×{TeamProgress.Multiplier(state.Streak):0.0}";
        inside.Children.Add(Faint(trainer, 14, new Thickness(0, 10, 0, 14)));
        inside.Children.Add(Rule());

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 10) };
        badges.Children.Add(Faint("Badges", 13, new Thickness(0, 0, 14, 0)));
        var earned = TeamProgress.Earned(Math.Max(0, state.LastTotal));
        for (var i = 0; i < TeamProgress.Badges.Length; i++)
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = $"{TeamProgress.Badges[i].Name}: {TeamProgress.Badges[i].Contributions} contributions" };
            if (i < earned) dot.Background = new SolidColorBrush(Color.FromRgb(0x7F, 0xD8, 0xBE));
            else if (i == earned) { dot.BorderThickness = new Thickness(1.5); dot.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7F, 0xD8, 0xBE)); }
            else dot.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            badges.Children.Add(dot);
        }
        if (earned < TeamProgress.Badges.Length)
            badges.Children.Add(Faint($"+{TeamProgress.Badges[earned].Contributions - Math.Max(0, state.LastTotal)} to {TeamProgress.Badges[earned].Name}", 13, new Thickness(10, 0, 0, 0)));
        inside.Children.Add(badges);

        for (var i = 0; i < state.Members.Count; i++)
            if (creatures.FirstOrDefault(creature => creature.Id == state.Members[i].Id) is { } creature)
                inside.Children.Add(MemberRow(creature, state.Members[i], i, i == state.Leader));

        inside.Children.Add(Rule(new Thickness(0, 14, 0, 14)));
        var bottom = new DockPanel();
        var share = Pill("Exp. Share", state.ExpShare, () => TeamService.Update(saved => saved.ExpShare = !saved.ExpShare));
        share.ToolTip = "On: every creature shares what you earn. Off: the leader takes it all, three times as much.";
        var walks = Pill("Walks the desktop", state.Walks, () => TeamService.Update(saved => saved.Walks = !saved.Walks));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(share); row.Children.Add(walks);
        bottom.Children.Add(row);
        inside.Children.Add(bottom);
        _body.Children.Add(card);
        _body.Children.Add(Faint("Click a creature to make it the leader.", 12, new Thickness(4, 10, 0, 18)));
        ShowAccount(state);
        ShowPack(state, installed: true);
    }

    /// <summary>Which GitHub account the team counts. Changing it starts the levels over: that is someone else's progress.</summary>
    private void ShowAccount(TeamState state)
    {
        _body.Children.Add(new TextBlock { Text = "GitHub account", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) });
        var account = new TextBox { Text = state.GitHubUser, Width = 420, ToolTip = "Leave empty to count the account signed in to the GitHub CLI (gh)." };
        AutomationProperties.SetName(account, "GitHub account");
        AutomationProperties.SetAutomationId(account, "TeamGitHubAccount");
        var use = new Button { Content = "Count this account", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(use, "TeamGitHubAccountUse");
        use.Click += (_, _) =>
        {
            var login = account.Text.Trim().TrimStart('@');
            if (login.Length > 0 && !GitHubContributions.IsValidAccount(login)) { _status.Text = $"\"{login}\" is not a GitHub account name."; return; }
            if (string.Equals(login, state.GitHubUser, StringComparison.OrdinalIgnoreCase)) { _ = Count(TimeSpan.Zero); return; }
            TeamService.Update(saved =>
            {
                saved.GitHubUser = login; saved.Login = login; saved.LastTotal = -1; saved.Streak = 0; saved.CountedAt = null;
                foreach (var member in saved.Members) member.Xp = 0;
            });
            _ = Count(TimeSpan.Zero);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(account); row.Children.Add(use);
        _body.Children.Add(row);
        _body.Children.Add(Faint("Empty counts whoever is signed in to the GitHub CLI. A name counts that account's public contributions, with or without the CLI. Changing it starts the levels over.", 12, new Thickness(0, 6, 0, 18)));
    }


    private FrameworkElement MemberRow(Creature creature, TeamMember member, int index, bool leader)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
        foreach (var width in new[] { 72.0, 220, 60 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 340 });
        var sprite = new Image { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Left };
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
        try { sprite.Source = new BitmapImage(new Uri(CreaturePack.FramePath(creature.Frames[0]))); } catch { }
        grid.Children.Add(sprite);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = creature.Name, FontSize = 18 });
        if (leader) line.Children.Add(new TextBlock { Text = "leader", FontSize = 13, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xD8, 0xBE)) });
        names.Children.Add(line);
        var types = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        foreach (var type in creature.Types)
        {
            if (types.Children.Count > 0) types.Children.Add(Faint("·", 13, new Thickness(6, 0, 6, 0)));
            types.Children.Add(new TextBlock { Text = type, FontSize = 14, Foreground = new SolidColorBrush(ColorOf(type)) });
        }
        names.Children.Add(types);
        Grid.SetColumn(names, 1); grid.Children.Add(names);

        var level = Faint("Lv " + TeamProgress.Level(member.Xp), 14, new Thickness(0));
        level.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(level, 2); grid.Children.Add(level);

        var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        var colors = creature.Types.Select(ColorOf).DefaultIfEmpty(Color.FromRgb(0x7F, 0xD8, 0xBE)).ToArray();
        var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left,
            Background = colors.Length > 1 ? new LinearGradientBrush(colors[0], colors[1], 0) : new SolidColorBrush(colors[0]) };
        var into = Math.Clamp(TeamProgress.Into(member.Xp), .02, 1);
        track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * into;
        track.Child = fill;
        track.ToolTip = $"{member.Xp:0} experience; level {TeamProgress.Level(member.Xp) + 1} at {TeamProgress.XpFor(TeamProgress.Level(member.Xp) + 1):0}";
        Grid.SetColumn(track, 3); grid.Children.Add(track);

        AutomationProperties.SetName(grid, $"{creature.Name}, level {TeamProgress.Level(member.Xp)}{(leader ? ", leader" : "")}");
        grid.MouseLeftButtonUp += (_, _) => TeamService.Update(saved => saved.Leader = index);
        return grid;
    }

    private void ShowEmpty(TeamState state)
    {
        _body.Children.Add(Faint("The creatures are not part of Belie: they come as a small pack of sprites, downloaded once. Belie's own pack has six critters drawn for it.", 14, new Thickness(0, 0, 0, 14)));
        ShowPack(state, installed: false);
    }

    private void ShowPack(TeamState state, bool installed)
    {
        var heading = new TextBlock { Text = "Creature pack", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) };
        _body.Children.Add(heading);
        var address = new TextBox { Text = state.PackUrl, Width = 420, ToolTip = "Leave empty for Belie's own pack. Another pack is a pack.json next to its PNG frames." };
        AutomationProperties.SetName(address, "Creature pack address");
        var bar = new System.Windows.Controls.ProgressBar { Height = 4, Maximum = 1, Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        var get = new Button { Content = installed ? "Download again" : "Download creatures", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(get, "CreaturePackDownload");
        get.Click += async (_, _) =>
        {
            if (_downloading != null) { _downloading.Cancel(); return; }
            using var cancel = _downloading = new CancellationTokenSource();
            get.Content = "Cancel"; bar.Visibility = Visibility.Visible; bar.Value = 0;
            try
            {
                var url = address.Text.Trim();
                var count = await CreaturePack.DownloadAsync(url, new Progress<double>(done => bar.Value = done), cancel.Token);
                TeamService.Update(saved => { saved.PackUrl = url; CreaturePack.Fill(saved, CreaturePack.Installed()); });
                _status.Text = $"{count} creatures arrived.";
                _ = Count(TimeSpan.Zero);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "The creatures could not be downloaded: " + ex.Message; Logger.Warn("Downloading creatures failed: " + ex.Message); }
            finally { _downloading = null; Show(); }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(address); row.Children.Add(get);
        _body.Children.Add(row);
        _body.Children.Add(bar);
        _body.Children.Add(Faint("Leave the address empty for Belie's own pack, from its GitHub repository.", 12, new Thickness(0, 6, 0, 0)));
    }

    private static Color ColorOf(string type)
    {
        if (TypeColors.TryGetValue(type, out var known)) return known;
        var hash = (uint)type.ToLowerInvariant().GetHashCode();
        return Color.FromRgb((byte)(120 + hash % 120), (byte)(120 + hash / 7 % 120), (byte)(120 + hash / 49 % 120));
    }

    private static TextBlock Faint(string text, double size, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = size, Margin = margin, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return block;
    }

    private static Border Rule(Thickness? margin = null)
    {
        var rule = new Border { Height = 1, Margin = margin ?? new Thickness(0) };
        rule.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        return rule;
    }

    private static Button Pill(string text, bool on, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0), FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas") };
        if (on) { button.Background = new SolidColorBrush(Color.FromRgb(0x3F, 0xB5, 0xC8)); button.Foreground = Brushes.Black; }
        AutomationProperties.SetItemStatus(button, on ? "On" : "Off");
        button.Click += (_, _) => click();
        return button;
    }
}
