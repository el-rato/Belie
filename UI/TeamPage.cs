using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace WallpaperProfiles.UI;

/// <summary>
/// Where the team is set up: which GitHub account it counts and where its creatures come from. The team itself lives
/// on the top bar, in the Team module.
/// </summary>
internal sealed class TeamPage : System.Windows.Controls.UserControl
{
    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly string _barFile;
    private readonly Action<string> _startBar;
    private CancellationTokenSource? _downloading;

    public TeamPage(string barFile, Action<string> startBar)
    {
        (_barFile, _startBar) = (barFile, startBar);
        var heading = new TextBlock { Text = "Team" };
        heading.SetResourceReference(StyleProperty, "WorkspaceHeading");
        var about = new TextBlock { Text = "Creatures that level up with your GitHub contributions. They live on the top bar: click the Team module to see them.", TextWrapping = TextWrapping.Wrap };
        about.SetResourceReference(StyleProperty, "WorkspaceCaption");
        _status.SetResourceReference(StyleProperty, "HintText");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        top.Children.Add(heading); top.Children.Add(about); top.Children.Add(_status);
        var layout = new StackPanel { Margin = new Thickness(24, 20, 24, 24), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch };
        layout.Children.Add(top); layout.Children.Add(_body);
        Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Again() => Dispatcher.BeginInvoke(Show);
        Loaded += (_, _) => { TeamService.Changed += Again; Show(); };
        Unloaded += (_, _) => TeamService.Changed -= Again;
    }

    internal void Show()
    {
        _body.Children.Clear();
        var state = CreaturePack.LoadState();
        var creatures = CreaturePack.Installed();
        ShowBar(creatures);
        if (creatures.Count > 0) ShowAccount(state);
        ShowPack(state, creatures);
    }

    /// <summary>Whether the bar has the Team module, and a way to add it.</summary>
    private void ShowBar(IReadOnlyList<Creature> creatures)
    {
        Heading("On the top bar");
        var store = new TopBarStore(_barFile);
        var bar = store.Load();
        if (creatures.Count > 0)
        {
            var team = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var creature in creatures.Take(TeamProgress.Size))
            {
                var sprite = new Image { Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0), ToolTip = creature.Name };
                RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
                CreatureSprite.Animate(sprite, creature);
                team.Children.Add(sprite);
            }
            _body.Children.Add(team);
        }
        if (bar.Enabled && bar.Shows(BarModuleKind.Team))
        {
            Hint("The Team module is on your bar. Click it to see levels, badges and your streak, choose the leader, and turn walking on or off.");
            return;
        }
        Hint(bar.Enabled ? "Add the Team module to your bar to see the team there. You can move it later under Top bar → Modules."
            : "The top bar is off. Adding the Team module turns it on.");
        var add = new Button { Content = "Add Team to the bar", Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(add, "TeamAddToBar");
        add.Click += (_, _) =>
        {
            try
            {
                var settings = store.Load();
                settings.Enabled = true;
                if (!settings.Shows(BarModuleKind.Team)) (settings.Right ??= new()).Insert(0, new BarModule(BarModuleKind.Team));
                store.Save(settings);
                _startBar(_barFile);
                _status.Text = "The Team module is on the bar.";
            }
            catch (Exception ex) { Logger.Error("Adding the Team module failed.", ex); _status.Text = "The module could not be added: " + ex.Message; }
            Show();
        };
        _body.Children.Add(add);
    }

    /// <summary>Which GitHub account the team counts. Changing it starts the levels over: that is someone else's progress.</summary>
    private void ShowAccount(TeamState state)
    {
        Heading("GitHub account");
        if (state.LastTotal >= 0) Hint($"Counting {(state.Login.Length > 0 ? state.Login : "your account")}: {state.LastTotal} contributions this year, a streak of {state.Streak}.");
        var account = new TextBox { Text = state.GitHubUser, Width = 420, ToolTip = "Leave empty to count the account signed in to the GitHub CLI (gh)." };
        AutomationProperties.SetName(account, "GitHub account");
        AutomationProperties.SetAutomationId(account, "TeamGitHubAccount");
        var use = new Button { Content = "Count this account", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(use, "TeamGitHubAccountUse");
        use.Click += async (_, _) =>
        {
            var login = account.Text.Trim().TrimStart('@');
            if (login.Length > 0 && !GitHubContributions.IsValidAccount(login)) { _status.Text = $"\"{login}\" is not a GitHub account name."; return; }
            if (!string.Equals(login, state.GitHubUser, StringComparison.OrdinalIgnoreCase))
                TeamService.Update(saved =>
                {
                    saved.GitHubUser = login; saved.Login = login; saved.LastTotal = -1; saved.Streak = 0; saved.CountedAt = null;
                    foreach (var member in saved.Members) member.Xp = 0;
                });
            _status.Text = "Counting contributions…";
            try { await TeamService.RefreshAsync(TimeSpan.Zero); _status.Text = "Counted."; }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(account); row.Children.Add(use);
        _body.Children.Add(row);
        Hint("Empty counts whoever is signed in to the GitHub CLI. A name counts that account's public contributions, with or without the CLI. Changing it starts the levels over.");
    }

    private void ShowPack(TeamState state, IReadOnlyList<Creature> creatures)
    {
        Heading("Creature pack");
        if (creatures.Count == 0) Hint("The creatures are not part of Belie: they come as a small pack of sprites, downloaded once. Belie's own pack has six critters drawn for it.");
        var address = new TextBox { Text = state.PackUrl, Width = 420, ToolTip = "Leave empty for Belie's own pack. Another pack is a pack.json next to its PNG frames." };
        AutomationProperties.SetName(address, "Creature pack address");
        var bar = new System.Windows.Controls.ProgressBar { Height = 4, Maximum = 1, Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        var get = new Button { Content = creatures.Count > 0 ? "Download again" : "Download creatures", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
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
                try { await TeamService.RefreshAsync(TimeSpan.Zero); } catch (Exception ex) { _status.Text += " " + ex.Message; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "The creatures could not be downloaded: " + ex.Message; Logger.Warn("Downloading creatures failed: " + ex.Message); }
            finally { _downloading = null; Show(); }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(address); row.Children.Add(get);
        _body.Children.Add(row);
        _body.Children.Add(bar);
        Hint("Leave the address empty for Belie's own pack, from its GitHub repository.");
    }

    private void Heading(string text) => _body.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, _body.Children.Count == 0 ? 0 : 18, 0, 8) });

    private void Hint(string text)
    {
        var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 2, 0, 8) };
        hint.SetResourceReference(StyleProperty, "HintText");
        _body.Children.Add(hint);
    }
}
