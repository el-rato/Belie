using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace WallpaperProfiles.UI;

/// <summary>The editors of one module and of one part of the bar.</summary>
internal sealed partial class TopBarPage
{
    private sealed record Field(string Property, string Label, string Hint, bool IsColor = false);

    private static readonly Field[] Fields =
    {
        new("color", "Text color", "#88c0d0, @accent or linear-gradient(90deg, #f0f, #0ff)", true), new("icon-color", "Icon color", "#f38ba8", true),
        new("background", "Background", "#1e1e2e, alpha(@background, .6) or linear-gradient(90deg, #f0f, #0ff)", true),
        new("font-family", "Font", "JetBrainsMono Nerd Font"), new("font-size", "Text size", "14px"), new("font-weight", "Text weight", "bold"),
        new("border", "Border", "1px solid @accent"), new("border-radius", "Roundness", "12px, or 12px 12px 0 0"),
        new("padding", "Space inside", "0 12px"), new("margin", "Space around", "4px 0"), new("min-width", "Smallest width", "60px"), new("opacity", "Opacity", "0.8"),
        new("spacing", "Space between modules", "16px"), new("shape", "Shape", "none, island or notch")
    };

    private static readonly (string Name, string Command)[] Actions =
    {
        ("Choose a wallpaper", "belie:wallpapers"), ("Next wallpaper", "belie:next-wallpaper"), ("Previous wallpaper", "belie:previous-wallpaper"),
        ("Play or pause", "belie:play"), ("Next track", "belie:next-track"), ("Previous track", "belie:previous-track"),
        ("Mute", "belie:mute"), ("Louder", "belie:volume-up"), ("Quieter", "belie:volume-down"),
        ("Next workspace", "belie:next-workspace"), ("Previous workspace", "belie:previous-workspace"), ("Bar settings", "belie:settings")
    };

    private readonly StackPanel _editor = new(), _partEditor = new();
    private readonly Dictionary<string, string>[] _styles = { new(), new(), new(), new() };
    private BarModule? _edited;

    private TextBox Input(string label, string id, string value, string hint, Action<string> set, FrameworkElement? extra = null)
    {
        var box = new TextBox { Text = value, Width = 240, ToolTip = hint.Length > 0 ? "For example: " + hint : null };
        box.TextChanged += (_, _) => { set(box.Text); Changed(); };
        Row(label, id, box, extra);
        return box;
    }

    /// <summary>Boxes for one set of style declarations. What cannot be understood is marked and not kept.</summary>
    private void StyleFields(string prefix, Dictionary<string, string> style, IEnumerable<string> properties)
    {
        foreach (var field in properties.Select(name => Fields.First(item => item.Property == name)))
        {
            var id = prefix + string.Concat(field.Property.Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
            var value = style.GetValueOrDefault(field.Property, "");
            if (field.Property == "font-family")
            {
                var font = FontBox(value, text => Take(style, "font-family", text, null));
                Row(field.Label, id, font);
                continue;
            }
            var box = new TextBox { Text = value, Width = 240, ToolTip = "For example: " + field.Hint };
            Button? swatch = null;
            if (field.IsColor)
            {
                swatch = new Button { Width = 34, Height = 34, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Choose a color" };
                var paint = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9) };
                swatch.Content = paint;
                AutomationProperties.SetName(swatch, "Choose " + field.Label.ToLowerInvariant());
                void Show() => paint.Background = Palette().TryBrush(box.Text) ?? System.Windows.Media.Brushes.Transparent;
                Show();
                box.TextChanged += (_, _) => Show();
                swatch.Click += (_, _) => PickColor(box);
            }
            box.TextChanged += (_, _) => Take(style, field.Property, box.Text, box);
            Row(field.Label, id, box, swatch);
        }
    }

    private void Take(Dictionary<string, string> style, string property, string value, TextBox? box)
    {
        value = value.Trim().TrimEnd(';');
        var fine = value.Length == 0 || new BarLook().Set(property, value, Palette(), 13);
        if (fine) box?.ClearValue(ForegroundProperty); else box?.SetResourceReference(ForegroundProperty, "DangerBrush");
        if (!fine) return;
        if (value.Length == 0) style.Remove(property); else style[property] = value;
        Changed();
    }

    /// <summary>Includes the colors the stylesheet names.</summary>
    private BarPalette Palette()
    {
        static System.Windows.Media.Color Of(string text) => SceneController.TryParseAccent(text, out var color) ? color : System.Windows.Media.Colors.Gray;
        var palette = new BarPalette(Of(_shown.BackgroundColor), Of(_shown.TextColor), Of(_shown.AccentColor), Of(_shown.BorderColor));
        palette.Define(BarStylesheet.Parse(_css.Text).Colors);
        return palette;
    }

    private void PickColor(TextBox box)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (Palette().TryColor(box.Text, out var color)) dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        if (Window.GetWindow(this) is not { } window) return;
        var owner = new System.Windows.Forms.NativeWindow();
        owner.AssignHandle(new WindowInteropHelper(window).Handle);
        try { if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK) box.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"; }
        finally { owner.ReleaseHandle(); }
    }

    private static ComboBox FontBox(string value, Action<string> changed)
    {
        var box = new ComboBox
        {
            IsEditable = true, IsTextSearchEnabled = false, ItemsSource = BarFonts.Names(), Text = value, Width = 240, MaxDropDownHeight = 320,
            ToolTip = "One font, or several separated by commas: the first one you have is used."
        };
        var last = value;
        void Take(string text)
        {
            if (text == last) return;
            last = text;
            changed(text);
        }
        box.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Take(box.Text)));
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string chosen) Take(chosen); };
        return box;
    }

    private void CommandField(string label, string id, string value, Action<string> set)
    {
        var pick = new Button { Content = "\uE70D", Width = 34, Height = 34, Padding = new Thickness(0), FontSize = 11, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Choose what it does" };
        pick.SetResourceReference(FontFamilyProperty, "IconFont");
        AutomationProperties.SetName(pick, "Choose: " + label);
        var box = Input(label, id, value, "wt.exe, https://example.org or belie:wallpapers", text => set(text.Trim()), pick);
        pick.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = pick, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (name, command) in Actions)
            {
                var entry = new System.Windows.Controls.MenuItem { Header = name };
                entry.Click += (_, _) => box.Text = command;
                menu.Items.Add(entry);
            }
            var none = new System.Windows.Controls.MenuItem { Header = "What it usually does" };
            none.Click += (_, _) => box.Text = "";
            menu.Items.Add(new Separator()); menu.Items.Add(none);
            menu.IsOpen = true;
        };
    }

    internal void Edit(BarModule? module)
    {
        _edited = module;
        _editor.Children.Clear();
        if (module == null) return;
        Into(_editor, () =>
        {
            Heading(NameOf(module.Kind));
            var kind = module.Kind;
            if (kind is BarModuleKind.Text or BarModuleKind.Launcher or BarModuleKind.Wallpaper or BarModuleKind.Separator)
                Input(kind == BarModuleKind.Separator ? "Symbol, or empty for a line" : "Text", "ModuleText", module.Text, kind == BarModuleKind.Separator ? "|" : "Hello", text => module.Text = text);
            if (kind == BarModuleKind.Launcher) CommandField("Opens", "ModuleCommand", module.Command, text => module.Command = text);
            if (kind == BarModuleKind.Script)
            {
                Input("Command", "ModuleCommand", module.Command, "echo Hello, or C:\\scripts\\mail.ps1", text => module.Command = text.Trim());
                Input("Run every (seconds)", "ModuleInterval", module.Interval.ToString(CultureInfo.CurrentCulture), "60",
                    text => { if (int.TryParse(text, out var seconds)) module.Interval = Math.Clamp(seconds, 1, 86400); });
                Hint("The first line the command writes is shown on the bar, and the rest when you point at it. It runs as you, hidden, and is stopped after ten seconds.");
            }
            if (kind is not (BarModuleKind.Workspaces or BarModuleKind.Island or BarModuleKind.Visualizer or BarModuleKind.Separator))
            {
                Input("Icon", "ModuleIcon", module.Icon, "any symbol or Nerd Font icon; - for none", text => module.Icon = text.Trim());
                if (kind is not (BarModuleKind.Media or BarModuleKind.Wallpaper)) Input("Format", "ModuleFormat", module.Format, "{icon} {value}", text => module.Format = text.Trim());
            }
            StyleFields("Module", module.Style, kind switch
            {
                BarModuleKind.Workspaces or BarModuleKind.Separator or BarModuleKind.Visualizer => new[] { "color", "background", "border", "border-radius", "padding", "margin", "opacity" },
                BarModuleKind.Island => new[] { "color", "background", "font-family", "font-size", "font-weight", "border", "border-radius" },
                _ => new[] { "color", "icon-color", "background", "font-family", "font-size", "font-weight", "border", "border-radius", "padding", "margin", "min-width", "opacity" }
            });
            if (kind != BarModuleKind.Workspaces && kind != BarModuleKind.Launcher) CommandField("Click", "ModuleClick", module.OnClick, text => module.OnClick = text);
            CommandField("Right-click", "ModuleRightClick", module.OnRightClick, text => module.OnRightClick = text);
            CommandField("Middle click", "ModuleMiddleClick", module.OnMiddleClick, text => module.OnMiddleClick = text);
            CommandField("Scroll up", "ModuleScrollUp", module.OnScrollUp, text => module.OnScrollUp = text);
            CommandField("Scroll down", "ModuleScrollDown", module.OnScrollDown, text => module.OnScrollDown = text);
            Input("Name in the stylesheet", "ModuleName", module.Name, "mail, to select it as #custom-mail",
                text => module.Name = string.Concat(text.Where(letter => char.IsLetterOrDigit(letter) || letter is '-' or '_')));
            var done = Press("Done", "ModuleDone", () => Edit(null));
            done.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            Add(done);
        });
    }

    private void EditPart(int part)
    {
        _partEditor.Children.Clear();
        Into(_partEditor, () => StyleFields("Part", _styles[part], part == 0
            ? new[] { "color", "background", "font-family", "font-size", "border", "border-radius", "padding", "opacity" }
            : new[] { "shape", "color", "background", "font-family", "font-size", "border", "border-radius", "padding", "margin", "spacing", "opacity" }));
    }

    private void BuildParts(TopBarSettings settings)
    {
        Heading("Parts");
        Hint("Each part can have its own shape and style, named as in a stylesheet. Empty boxes follow the choices above.");
        var sources = new[] { settings.BarStyle, settings.LeftStyle, settings.CenterStyle, settings.RightStyle };
        for (var i = 0; i < sources.Length; i++)
        {
            _styles[i].Clear();
            foreach (var (name, value) in sources[i]) _styles[i][name] = value;
        }
        var part = new ComboBox { ItemsSource = new[] { "Whole bar", "Left", "Center", "Right" }, SelectedIndex = 0, Width = 220 };
        part.SelectionChanged += (_, _) => EditPart(Math.Max(0, part.SelectedIndex));
        Row("Part", "TopBarPart", part);
        Detach(_partEditor); Add(_partEditor);
        EditPart(0);
        OnSave(to => (to.BarStyle, to.LeftStyle, to.CenterStyle, to.RightStyle) = (new(_styles[0]), new(_styles[1]), new(_styles[2]), new(_styles[3])));
    }

    private static void Detach(FrameworkElement element) { if (element.Parent is System.Windows.Controls.Panel panel) panel.Children.Remove(element); }
}
