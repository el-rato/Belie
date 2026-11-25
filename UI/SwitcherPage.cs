using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using TextBox = System.Windows.Controls.TextBox;

namespace WallpaperProfiles.UI;

/// <summary>Settings of the wallpaper switcher, with a working copy of it.</summary>
internal sealed class SwitcherPage : SettingsPage<AppSettings>
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly Func<(IReadOnlyList<string> Items, string Current)> _pictures;
    private readonly Action _open;
    private readonly Func<string> _keysChanged;
    private readonly SwitcherDeck _deck = new() { ClipToBounds = true, Height = 300 };
    private Func<SwitcherLook>? _look;
    private Func<System.Windows.Media.Brush>? _outline;
    private TextBlock? _problem;
    private bool _loaded;

    /// <param name="keysChanged">Takes the new keys into use, and returns what is wrong with them.</param>
    public SwitcherPage(SettingsStore store, AppSettings settings, Func<(IReadOnlyList<string>, string)> pictures, Action open, Func<string> keysChanged)
        : base("Wallpaper switcher", "Pick any wallpaper of the profile you are on. A slideshow carries on from the one you pick.")
    {
        _store = store; _settings = settings; _pictures = pictures; _open = open; _keysChanged = keysChanged;
        AutomationProperties.SetAutomationId(_deck, "SwitcherPreview");
        IsVisibleChanged += async (_, _) =>
        {
            if (!IsVisible || _loaded) return;
            _loaded = true;
            try
            {
                var (items, current) = await Task.Run(_pictures);
                _deck.Show(items, current);
            }
            catch (Exception ex) { Logger.Error("The switcher preview failed.", ex); }
        };
        Reload();
    }

    protected override AppSettings Load() => _settings;

    protected override void Store(AppSettings settings)
    {
        _store.Save(settings);
        var problem = _keysChanged();
        if (_problem == null) return;
        _problem.Text = problem;
        _problem.Visibility = problem.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void Build(AppSettings settings)
    {
        if (_deck.Parent is Border before) before.Child = null;
        Tab("Look");
        var open = Press("Open the switcher", "SwitcherOpen", () => { SaveNow(); _open(); });
        open.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        Add(open);
        var frame = new Border { Child = _deck, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 4, 0, 12), ClipToBounds = true };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        Add(frame);
        var layout = Pick("Layout", "SwitcherLayout", new[] { new Choice<SwitcherLayout>(SwitcherLayout.Slanted, "Slanted, one large"), new(SwitcherLayout.Carousel, "Carousel"),
            new(SwitcherLayout.Filmstrip, "Filmstrip"), new(SwitcherLayout.Grid, "Grid") }, settings.SwitcherLayout, (to, value) => to.SwitcherLayout = value);
        Pick("Behind the cards", "SwitcherBackdrop", new[] { new Choice<SwitcherBackdrop>(SwitcherBackdrop.None, "Your desktop"), new(SwitcherBackdrop.Dimmed, "Your desktop, dimmed"),
            new(SwitcherBackdrop.Preview, "The wallpaper, full size"), new(SwitcherBackdrop.Blurred, "The wallpaper, blurred") }, settings.SwitcherBackdrop, (to, value) => to.SwitcherBackdrop = value);
        var size = Slide("Size", "SwitcherSize", 12, 70, 1, Math.Round(settings.SwitcherSize * 100), "%", (to, value) => to.SwitcherSize = value / 100);
        var slant = Slide("Slant", "SwitcherSlant", -35, 35, 1, settings.SwitcherSlant, "°", (to, value) => to.SwitcherSlant = value);
        var sides = Slide("Cards on each side", "SwitcherSides", 1, 5, 1, settings.SwitcherSides, "", (to, value) => to.SwitcherSides = (int)Math.Round(value));
        var spacing = Slide("Space between cards", "SwitcherSpacing", -40, 80, 1, settings.SwitcherSpacing, " px", (to, value) => to.SwitcherSpacing = value);
        var radius = Slide("Roundness", "SwitcherRadius", 0, 60, 1, settings.SwitcherRadius, " px", (to, value) => to.SwitcherRadius = value);
        var outline = Slide("Outline", "SwitcherOutline", 0, 6, .5, settings.SwitcherOutline, " px", (to, value) => to.SwitcherOutline = value);
        var color = Color("Outline color", "SwitcherOutlineColor", settings.SwitcherOutlineColor, (to, value) => to.SwitcherOutlineColor = value, optional: true);
        Slide("Place on the screen", "SwitcherPosition", 0, 100, 5, Math.Round(settings.SwitcherPosition * 100), "%", (to, value) => to.SwitcherPosition = value / 100);
        Hint("0% is the top of the screen, 100% the bottom.");
        var speed = Slide("Speed", "SwitcherSpeed", 0, 400, 25, Math.Round(settings.SwitcherSpeed * 100), "%", (to, value) => to.SwitcherSpeed = value / 100);
        Switch("Show names", "SwitcherNames", settings.SwitcherNames, (to, value) => to.SwitcherNames = value);
        Switch("Show the colors each wallpaper gives the bar", "SwitcherColors", settings.SwitcherColors, (to, value) => to.SwitcherColors = value);
        Switch("Stay open after choosing", "SwitcherStaysOpen", settings.SwitcherStaysOpen, (to, value) => to.SwitcherStaysOpen = value);

        Tab("Keys");
        Keys("Open the switcher", "SwitcherKeys", settings.SwitcherKeys, (to, value) => to.SwitcherKeys = value);
        Keys("Next wallpaper", "SwitcherNextKeys", settings.NextWallpaperKeys, (to, value) => to.NextWallpaperKeys = value);
        Keys("Previous wallpaper", "SwitcherPreviousKeys", settings.PreviousWallpaperKeys, (to, value) => to.PreviousWallpaperKeys = value);
        Hint("Click a box and press the keys. Backspace turns them off.");
        _problem = Hint("");
        _problem.Visibility = Visibility.Collapsed;
        Heading("In the switcher");
        Hint("Arrows or the wheel move along, Enter or a click on the large card chooses, Tab looks through your other profiles, R jumps somewhere random, and Esc closes.");
        Heading("From the bar");
        Hint("Add the Wallpaper module on the Top bar page: click it to open the switcher, scroll over it to step through the wallpapers.");

        _look = () => new SwitcherLook(((Choice<SwitcherLayout>)layout.SelectedItem).Value, size.Value / 100, slant.Value, spacing.Value * .4, radius.Value * .4, outline.Value * .7, (int)sides.Value, speed.Value / 100);
        _outline = () => new SolidColorBrush(SceneController.TryParseAccent(color.Text, out var chosen) ? chosen : System.Windows.Media.Color.FromRgb(12, 13, 16));
        void Show() => _deck.Apply(_look(), _outline());
        layout.SelectionChanged += (_, _) => Show();
        foreach (var slider in new[] { size, slant, sides, spacing, radius, outline, speed }) slider.ValueChanged += (_, _) => Show();
        color.TextChanged += (_, _) => Show();
        Show();
    }

    private void Keys(string label, string id, string value, Action<AppSettings, string> set)
    {
        var box = new TextBox { Text = value, Width = 220, IsReadOnly = true, ToolTip = "Click, then press the keys" };
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Tab) return;
            e.Handled = true;
            if (key is Key.Back or Key.Delete) { box.Text = ""; Changed(); return; }
            try
            {
                var text = ProfileShortcutManager.Format(new KeyGesture(key, Keyboard.Modifiers));
                if (ProfileShortcutManager.TryParse(text, out KeyGesture? _, out var error)) { box.Text = text; Changed(); }
                else if (key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)) Status = error;
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException) { }
        };
        OnSave(settings => set(settings, box.Text));
        Row(label, id, box);
    }
}
