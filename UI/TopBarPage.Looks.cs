using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Image = System.Windows.Controls.Image;
using TextBox = System.Windows.Controls.TextBox;

namespace WallpaperProfiles.UI;

/// <summary>Complete looks, and the stylesheet.</summary>
internal sealed partial class TopBarPage
{
    private const string PreviousName = "Previous look";
    private const string Example = """
        /* Colors that other rules can use. The bar's own are @background, @text, @accent and @border. */
        @define-color pink #f5c2e7;

        bar { background: transparent; }
        .left, .right { background: alpha(@background, .7); border-radius: 12px; padding: 0 12px; }
        .center { shape: notch; background: #0b0c0f; border-radius: 14px; padding: 0 18px; }

        #clock { color: @pink; font-weight: bold; }
        #volume:hover { background: alpha(@accent, .25); border-radius: 8px; }
        #workspaces button.active { background: linear-gradient(90deg, @accent, @pink); }

        """;

    private readonly List<(Image Picture, BarRice Rice)> _cards = new();
    private readonly TextBox _css = new()
    {
        AcceptsReturn = true, AcceptsTab = true, Height = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(10)
    };
    private readonly TextBlock _cssProblems = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 8) };
    private readonly DispatcherTimer _cssTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private TopBarSettings _shown = new();
    private WallpaperColors? _wallpaperColors;
    private BitmapSource? _wallpaper;
    private bool _readingCss, _drawing;
    private int _drawn;

    /// <summary>Asks whether a look's commands may be kept: true keeps them, false removes them, null changes nothing.</summary>
    internal Func<BarRice, bool?> AskAboutCommands { get; set; } = rice => System.Windows.MessageBox.Show(
        $"“{rice.Name}” would run these commands on your computer:\n\n{string.Join("\n", rice.Commands.Take(20))}{(rice.Commands.Count > 20 ? "\n…" : "")}\n\nKeep them only if you trust where this look came from. Choose No to use the look without them.",
        "Commands in this look", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No) switch
    { MessageBoxResult.Yes => true, MessageBoxResult.No => false, _ => null };

    private BarRiceStore Looks => new(Path.Combine(Path.GetDirectoryName(Bar.FilePath)!, "bar-looks"));

    private void StartLooks()
    {
        AutomationProperties.SetAutomationId(_css, "TopBarCss"); AutomationProperties.SetName(_css, "Stylesheet");
        _css.TextChanged += (_, _) =>
        {
            if (_readingCss) return;
            ShowCssProblems();
            _cssTimer.Stop(); _cssTimer.Start();
        };
        _cssTimer.Tick += (_, _) => SaveCss();
        IsVisibleChanged += (_, _) => { if (!IsVisible) SaveCss(); else { ReadCss(); DrawLooks(); } };
        Unloaded += (_, _) => SaveCss();
        // Coming back from another editor shows what was written there.
        IsKeyboardFocusWithinChanged += (_, e) => { if ((bool)e.NewValue) ReadCss(); };
    }

    protected override TopBarSettings Load()
    {
        SaveCss();
        return _shown = base.Load();
    }

    private void ReadCss()
    {
        if (_cssTimer.IsEnabled) return;
        var text = Bar.LoadCss();
        if (text == _css.Text) return;
        _readingCss = true;
        _css.Text = text;
        _readingCss = false;
        ShowCssProblems();
    }

    internal void SaveCss()
    {
        if (!_cssTimer.IsEnabled) return;
        _cssTimer.Stop();
        try { Bar.SaveCss(_css.Text); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "The stylesheet could not be saved: " + ex.Message; }
    }

    private void ShowCssProblems()
    {
        var problems = BarStylesheet.Parse(_css.Text).Problems;
        _cssProblems.Text = problems.Count == 0 ? "" : string.Join("\n", problems.Take(8)) + (problems.Count > 8 ? $"\nand {problems.Count - 8} more" : "");
        _cssProblems.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BuildLooks()
    {
        Tab("Looks");
        Hint("A look changes everything the bar shows and how it is drawn. Your workspaces, profiles and tiling stay as they are, and the look you had is kept below.");
        _cards.Clear(); _drawn = 0;
        foreach (var rice in BarRice.BuiltIn) Add(LookCard(rice, null));
        Heading("Yours");
        var buttons = new WrapPanel();
        var name = new TextBox { Width = 200, Text = "My look", Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAutomationId(name, "LookName"); AutomationProperties.SetName(name, "Name of the look");
        buttons.Children.Add(name);
        buttons.Children.Add(Press("Save this look", "LookSave", () => SaveLook(name.Text)));
        buttons.Children.Add(Press("Open a look…", "LookImport", ImportLook));
        buttons.Children.Add(Press("Share this look…", "LookExport", () => ExportLook(name.Text)));
        Add(buttons);
        foreach (var (path, rice) in Looks.Load()) Add(LookCard(rice, path));
        if (IsVisible) DrawLooks();
    }

    private FrameworkElement LookCard(BarRice rice, string? path)
    {
        var picture = new Image { Stretch = Stretch.Uniform, MinHeight = 44 };
        _cards.Add((picture, rice));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = rice.Name, FontWeight = FontWeights.SemiBold });
        if (rice.About.Length > 0)
        {
            var about = new TextBlock { Text = rice.About, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            about.SetResourceReference(StyleProperty, "HintText");
            words.Children.Add(about);
        }
        var use = Press("Use", (path == null ? "Look" : "OwnLook") + rice.Name.Replace(" ", ""), () => UseLook(rice));
        use.Margin = new Thickness(12, 0, 0, 0); use.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(use, "Use " + rice.Name);
        var row = new DockPanel { Margin = new Thickness(14, 10, 14, 12) };
        DockPanel.SetDock(use, Dock.Right); row.Children.Add(use);
        if (path != null)
        {
            var remove = Press("Remove", "RemoveLook" + rice.Name.Replace(" ", ""), () => RemoveLook(path, rice.Name));
            remove.Margin = new Thickness(12, 0, 0, 0); remove.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
        }
        row.Children.Add(words);
        var body = new StackPanel();
        body.Children.Add(picture); body.Children.Add(row);
        var card = new Border { Child = body, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 12) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        return card;
    }

    /// <summary>One picture at a time, so that the page stays quick.</summary>
    private async void DrawLooks()
    {
        if (_drawing) return;
        _drawing = true;
        try
        {
            if (_wallpaper == null)
            {
                var path = WallpaperTransition.CurrentWallpaper().Path;
                if (File.Exists(path)) (_wallpaper, _wallpaperColors) = await Task.Run(() => (Small(path), WallpaperColors.Read(path)));
            }
        }
        catch (Exception ex) { Logger.Warn("The wallpaper could not be read for the looks: " + ex.Message); }
        try { while (IsVisible && DrawNext()) await Dispatcher.Yield(DispatcherPriority.Background); }
        finally { _drawing = false; }
    }

    internal bool DrawNext()
    {
        if (_drawn >= _cards.Count) return false;
        var (picture, rice) = _cards[_drawn++];
        try { picture.Source = BarPreview.Render(rice.ApplyTo(_shown), _wallpaperColors, _wallpaper); }
        catch (Exception ex) { Logger.Error("The look " + rice.Name + " could not be drawn.", ex); }
        return true;
    }

    private static BitmapSource Small(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path); image.DecodePixelWidth = 1000; image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit(); image.Freeze();
        return image;
    }

    internal void UseLook(BarRice rice)
    {
        SaveNow();
        try
        {
            var current = Load();
            Looks.Save(BarRice.From(PreviousName, current));
            var next = rice.ApplyTo(current);
            // The stylesheet goes first, and the bar is told again once the settings are saved.
            Bar.SaveCss(rice.Css);
            Store(next);
            ReadCss();
            Reload();
            Status = rice.Name + " is on the bar." + (current.Enabled ? "" : " Turn the bar on in Modules to see it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "This look could not be used: " + ex.Message; }
    }

    internal void SaveLook(string name)
    {
        name = name.Trim();
        if (name.Length == 0) { Status = "Give the look a name."; return; }
        SaveNow();
        try
        {
            Looks.Save(BarRice.From(name, Load()));
            Reload();
            Status = $"Saved as {name}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "This look could not be saved: " + ex.Message; }
    }

    private void RemoveLook(string path, string name)
    {
        if (System.Windows.MessageBox.Show($"Remove the look “{name}”?", "Remove look", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try { File.Delete(path); Reload(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "This look could not be removed: " + ex.Message; }
    }

    private void ImportLook()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Open a look", Filter = "Bar look (*.bar.json)|*.bar.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) ImportLook(dialog.FileName);
    }

    internal void ImportLook(string path)
    {
        try
        {
            var rice = BarRiceStore.Read(path);
            if (rice.Commands.Count > 0)
            {
                if (AskAboutCommands(rice) is not { } keep) return;
                if (!keep) rice.RemoveCommands();
            }
            SaveNow();
            Looks.Save(rice);
            Reload();
            Status = rice.Name + " was added below.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException) { Status = "This look could not be opened: " + ex.Message; }
    }

    private void ExportLook(string name)
    {
        SaveNow();
        name = name.Trim();
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Share this look", FileName = (name.Length > 0 ? name : "My look") + BarRiceStore.Extension, Filter = "Bar look (*.bar.json)|*.bar.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            BarRiceStore.Write(dialog.FileName, BarRice.From(name, Load()));
            Status = "Saved. It holds the look and its commands, but none of your profiles, workspace names, apps or weather place.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "This look could not be saved: " + ex.Message; }
    }

    private void BuildStylesheet()
    {
        Tab("Stylesheet");
        Hint("CSS, in the part of it that Waybar users know. It has the last word over every other choice, and the bar follows as you type or save the file in any editor.");
        Detach(_css); Detach(_cssProblems);
        Add(_css);
        _cssProblems.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        Add(_cssProblems);
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(Press("Add an example", "TopBarCssExample", () => _css.Text = Example + _css.Text));
        buttons.Children.Add(Press("Open the file", "TopBarCssOpen", OpenCss));
        buttons.Children.Add(Press("Open the fonts folder", "TopBarFonts", OpenFonts));
        Add(buttons);
        Heading("What can be selected");
        Hint("bar (or window#waybar): the whole bar.   .left  .center  .right: its parts.\n"
            + "#workspaces  #title  #clock  #date  #media  #visualizer  #cpu  #memory  #disk  #network  #volume  #battery  #uptime  #weather  #island  #wallpaper: modules.   #custom-name: a module you have named.   .module: all of them.\n"
            + "#workspaces button.active, .occupied, .empty: the workspaces.   #volume icon: a module's icon.   popup: what opens from the bar.   :hover: while the mouse is there.");
        Heading("What can be set");
        Hint("color, icon-color, background, border, border-color, border-radius, padding, margin, font-family, font-size, font-weight, font-style, opacity, width, min-width, height, spacing, shape: none | island | notch.\n"
            + "Colors can be #hex, rgba(), a name, @accent, alpha(@accent, .5), mix(@accent, white, .3), shade(), lighter(), darker() or linear-gradient(). Name your own with @define-color.");
        ReadCss(); ShowCssProblems();
    }

    private void OpenCss()
    {
        SaveCss();
        try
        {
            if (!File.Exists(Bar.CssPath)) File.WriteAllText(Bar.CssPath, _css.Text.Length > 0 ? _css.Text : Example);
            ReadCss();
            // Not whatever opens stylesheets, which may be a browser.
            using var editor = Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { Bar.CssPath }, UseShellExecute = false });
        }
        catch (Exception ex) { Status = "The stylesheet could not be opened: " + ex.Message; }
    }

    private void OpenFonts()
    {
        try
        {
            Directory.CreateDirectory(BarFonts.Folder);
            Process.Start(new ProcessStartInfo(BarFonts.Folder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Status = "The fonts folder could not be opened: " + ex.Message; }
    }
}
