using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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

namespace WallpaperProfiles.UI;

/// <summary>Counts contributions and saves the team. The tray app watches the file and moves the walker to match.</summary>
internal static class TeamService
{
    private static readonly SemaphoreSlim Counting = new(1);

    public static event Action? Changed;

    public static void Update(Action<TeamState> change)
    {
        var state = CreaturePack.LoadState();
        change(state);
        CreaturePack.SaveState(state);
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

    internal static BitmapSource? Sprite(Creature creature)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(CreaturePack.FramePath(creature.Frames[0])); image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}

/// <summary>
/// The team card that drops down from the bar's Team module. It takes the bar's colors, because the bar runs in a
/// process of its own without the main window's theme.
/// </summary>
internal sealed class TeamPanel : System.Windows.Controls.UserControl
{
    public const double PanelWidth = 640, PanelHeight = 540;
    private static readonly Color Mint = Color.FromRgb(0x7F, 0xD8, 0xBE);
    private static readonly Dictionary<string, Color> TypeColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Leaf"] = Color.FromRgb(0x7C, 0xCB, 0x6F), ["Ember"] = Color.FromRgb(0xF0, 0x8A, 0x4B), ["Tide"] = Color.FromRgb(0x64, 0xAA, 0xF0),
        ["Spark"] = Color.FromRgb(0xF5, 0xD5, 0x47), ["Bug"] = Color.FromRgb(0xB5, 0xC0, 0x4A), ["Shade"] = Color.FromRgb(0x9A, 0x6E, 0xD2),
        ["Air"] = Color.FromRgb(0xB8, 0xA8, 0xF0), ["Stone"] = Color.FromRgb(0xA0, 0xA0, 0xAA)
    };

    private readonly Brush _text, _faint, _line, _accent;
    private readonly StackPanel _body = new();
    private string _status = "";

    public TeamPanel(Brush text, Brush accent)
    {
        _text = text; _accent = accent;
        var color = text is SolidColorBrush { Color: var shade } ? shade : Colors.White;
        _faint = Frozen(Color.FromArgb(160, color.R, color.G, color.B));
        _line = Frozen(Color.FromArgb(36, color.R, color.G, color.B));
        Content = _body;
        void Again() => Dispatcher.BeginInvoke(Show);
        Loaded += (_, _) => { TeamService.Changed += Again; Show(); _ = Count(TimeSpan.FromMinutes(30)); };
        Unloaded += (_, _) => TeamService.Changed -= Again;
    }

    private async Task Count(TimeSpan ifOlderThan)
    {
        if (CreaturePack.Installed().Count == 0) return;
        try { await TeamService.RefreshAsync(ifOlderThan); _status = ""; }
        catch (Exception ex) { _status = ex.Message; Logger.Warn("Counting contributions failed: " + ex.Message); Show(); }
    }

    private void Show()
    {
        _body.Children.Clear();
        var creatures = CreaturePack.Installed();
        var state = CreaturePack.LoadState();
        if (creatures.Count == 0)
        {
            _body.Children.Add(Words("Team", 22, _text));
            _body.Children.Add(Words("No creatures yet. Download them in Belie, on the Team page.", 14, _faint, new Thickness(0, 10, 0, 0)));
            return;
        }
        if (state.Members.Count < Math.Min(TeamProgress.Size, creatures.Count)) { CreaturePack.Fill(state, creatures); CreaturePack.SaveState(state); }

        var title = new DockPanel();
        var refresh = Pill("Refresh", false, () => _ = Count(TimeSpan.Zero));
        DockPanel.SetDock(refresh, Dock.Right);
        title.Children.Add(refresh);
        title.Children.Add(Words("Team", 22, _text));
        _body.Children.Add(title);
        var trainer = state.LastTotal < 0 ? "Not counted yet"
            : $"{(state.Login.Length > 0 ? state.Login : "Trainer")}  ·  {state.LastTotal} contributions  ·  streak {state.Streak} ×{TeamProgress.Multiplier(state.Streak):0.0}";
        _body.Children.Add(Words(_status.Length > 0 ? _status : trainer, 14, _faint, new Thickness(0, 10, 0, 14)));
        _body.Children.Add(Rule());

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 10) };
        badges.Children.Add(Words("Badges", 13, _faint, new Thickness(0, 0, 14, 0)));
        var earned = TeamProgress.Earned(Math.Max(0, state.LastTotal));
        for (var i = 0; i < TeamProgress.Badges.Length; i++)
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"{TeamProgress.Badges[i].Name}: {TeamProgress.Badges[i].Contributions} contributions" };
            if (i < earned) dot.Background = new SolidColorBrush(Mint);
            else if (i == earned) { dot.BorderThickness = new Thickness(1.5); dot.BorderBrush = new SolidColorBrush(Mint); }
            else dot.Background = _line;
            badges.Children.Add(dot);
        }
        if (earned < TeamProgress.Badges.Length)
            badges.Children.Add(Words($"+{TeamProgress.Badges[earned].Contributions - Math.Max(0, state.LastTotal)} to {TeamProgress.Badges[earned].Name}", 13, _faint, new Thickness(10, 0, 0, 0)));
        _body.Children.Add(badges);

        for (var i = 0; i < state.Members.Count; i++)
            if (creatures.FirstOrDefault(creature => creature.Id == state.Members[i].Id) is { } creature)
                _body.Children.Add(MemberRow(creature, state.Members[i], i, i == state.Leader));

        _body.Children.Add(Rule(new Thickness(0, 12, 0, 14)));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var share = Pill("Exp. Share", state.ExpShare, () => TeamService.Update(saved => saved.ExpShare = !saved.ExpShare));
        share.ToolTip = "On: every creature shares what you earn. Off: the leader takes it all, three times as much.";
        row.Children.Add(share);
        row.Children.Add(Pill("Walks the desktop", state.Walks, () => TeamService.Update(saved => saved.Walks = !saved.Walks)));
        _body.Children.Add(row);
    }

    private FrameworkElement MemberRow(Creature creature, TeamMember member, int index, bool leader)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, ToolTip = leader ? null : "Make " + creature.Name + " the leader" };
        foreach (var width in new[] { 64.0, 200, 56 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sprite = new Image { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Left, Source = TeamService.Sprite(creature) };
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
        grid.Children.Add(sprite);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Words(creature.Name, 17, _text));
        if (leader) line.Children.Add(Words("leader", 13, new SolidColorBrush(Mint), new Thickness(8, 0, 0, 0)));
        names.Children.Add(line);
        var types = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var type in creature.Types)
        {
            if (types.Children.Count > 0) types.Children.Add(Words("·", 13, _faint, new Thickness(6, 0, 6, 0)));
            types.Children.Add(Words(type, 13, new SolidColorBrush(ColorOf(type))));
        }
        names.Children.Add(types);
        Grid.SetColumn(names, 1); grid.Children.Add(names);

        var level = Words("Lv " + TeamProgress.Level(member.Xp), 14, _faint);
        Grid.SetColumn(level, 2); grid.Children.Add(level);

        var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Background = _line };
        var colors = creature.Types.Select(ColorOf).DefaultIfEmpty(Mint).ToArray();
        var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left,
            Background = colors.Length > 1 ? new LinearGradientBrush(colors[0], colors[1], 0) : new SolidColorBrush(colors[0]) };
        var into = Math.Clamp(TeamProgress.Into(member.Xp), .02, 1);
        track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * into;
        track.Child = fill;
        track.ToolTip = $"{member.Xp:0} experience; level {TeamProgress.Level(member.Xp) + 1} at {TeamProgress.XpFor(TeamProgress.Level(member.Xp) + 1):0}";
        Grid.SetColumn(track, 3); grid.Children.Add(track);

        AutomationProperties.SetName(grid, $"{creature.Name}, level {TeamProgress.Level(member.Xp)}{(leader ? ", leader" : "")}");
        grid.MouseLeftButtonUp += (_, e) => { e.Handled = true; TeamService.Update(saved => saved.Leader = index); };
        return grid;
    }

    internal static Color ColorOf(string type)
    {
        if (TypeColors.TryGetValue(type, out var known)) return known;
        var hash = (uint)type.ToLowerInvariant().GetHashCode();
        return Color.FromRgb((byte)(120 + hash % 120), (byte)(120 + hash / 7 % 120), (byte)(120 + hash / 49 % 120));
    }

    private static TextBlock Words(string text, double size, Brush color, Thickness margin = default)
        => new() { Text = text, FontSize = size, Foreground = color, Margin = margin, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private Border Rule(Thickness? margin = null) => new() { Height = 1, Margin = margin ?? new Thickness(0), Background = _line };

    private Button Pill(string text, bool on, Action click)
    {
        var chip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 8, 16, 8), Background = on ? _accent : _line,
            Child = new TextBlock { Text = text, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 14, Foreground = on ? Brushes.Black : _text } };
        var button = new Button { Content = chip, Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) } };
        AutomationProperties.SetName(button, text);
        AutomationProperties.SetItemStatus(button, on ? "On" : "Off");
        button.Click += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    private static SolidColorBrush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
}
