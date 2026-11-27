using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;
using TextBox = System.Windows.Controls.TextBox;

namespace WallpaperProfiles.UI;

/// <summary>
/// A page of settings that saves itself. Every save starts from what is stored now and changes only what this page
/// shows, so pages that share a file do not undo each other.
/// </summary>
internal abstract class SettingsPage<T> : System.Windows.Controls.UserControl where T : class
{
    protected sealed record Choice<TValue>(TValue Value, string Name) { public override string ToString() => Name; }
    private sealed record Owner(IntPtr Handle) : System.Windows.Forms.IWin32Window;

    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 18, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _saving = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly List<Action<T>> _apply = new();
    private StackPanel _panel = new();
    private bool _building;

    protected SettingsPage(string title, string description)
    {
        _tabs.SetResourceReference(StyleProperty, "StudioTabs");
        var heading = new TextBlock { Text = title };
        heading.SetResourceReference(StyleProperty, "WorkspaceHeading");
        var about = new TextBlock { Text = description };
        about.SetResourceReference(StyleProperty, "WorkspaceCaption");
        _status.SetResourceReference(StyleProperty, "HintText");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        top.Children.Add(heading); top.Children.Add(about); top.Children.Add(_status);
        _status.Margin = new Thickness(0, 6, 0, 0);
        var layout = new DockPanel { Margin = new Thickness(24, 20, 24, 12), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Stretch };
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top); layout.Children.Add(_tabs);
        Content = layout;
        _saving.Tick += (_, _) => SaveNow();
        // Leaving the page must not lose a change that is still waiting.
        IsVisibleChanged += (_, _) => { if (!IsVisible && _saving.IsEnabled) SaveNow(); };
        Unloaded += (_, _) => { if (_saving.IsEnabled) SaveNow(); };
    }

    protected abstract T Load();
    protected abstract void Store(T settings);
    protected abstract void Build(T settings);
    protected string Status { set => _status.Text = value; }

    /// <summary>Shows what is stored now.</summary>
    public void Reload()
    {
        if (_saving.IsEnabled) SaveNow();
        var tab = Math.Max(0, _tabs.SelectedIndex);
        _building = true;
        _tabs.Items.Clear(); _apply.Clear(); _status.Text = "";
        Build(Load());
        _tabs.SelectedIndex = Math.Min(tab, _tabs.Items.Count - 1);
        _building = false;
    }

    protected void Changed()
    {
        if (_building) return;
        _saving.Stop(); _saving.Start();
    }

    internal bool SaveNow()
    {
        _saving.Stop();
        var problems = new List<string>();
        try
        {
            var settings = Load();
            foreach (var apply in _apply)
                try { apply(settings); }
                catch (FormatException ex) { problems.Add(ex.Message); }
            Store(settings);
            _status.Text = problems.Count > 0 ? string.Join(" ", problems) : "Saved.";
            return problems.Count == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("Saving settings failed.", ex);
            _status.Text = "This could not be saved: " + ex.Message;
            return false;
        }
    }

    protected void Tab(string name)
    {
        _panel = new StackPanel { Margin = new Thickness(0, 0, 14, 24) };
        // Tabs color their own text, which must not reach what is inside.
        _panel.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimaryBrush");
        _tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
    }

    /// <summary>Puts what is added meanwhile into a panel of its own.</summary>
    protected void Into(StackPanel panel, Action build)
    {
        var outer = _panel;
        _panel = panel;
        try { build(); }
        finally { _panel = outer; }
    }

    protected void Add(UIElement element) => _panel.Children.Add(element);
    protected void OnSave(Action<T> apply) => _apply.Add(apply);

    protected void Heading(string text) => Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, _panel.Children.Count == 0 ? 0 : 22, 0, 8) });

    protected TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 2, 0, 8) };
        hint.SetResourceReference(StyleProperty, "HintText");
        Add(hint);
        return hint;
    }

    protected DockPanel Row(string label, string id, FrameworkElement control, FrameworkElement? extra = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), MinHeight = 34 };
        FrameworkElement right = control;
        if (extra != null)
        {
            var pair = new StackPanel { Orientation = Orientation.Horizontal };
            pair.Children.Add(control); pair.Children.Add(extra);
            right = pair;
        }
        DockPanel.SetDock(right, Dock.Right);
        right.VerticalAlignment = control.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(control, id);
        AutomationProperties.SetName(control, label);
        row.Children.Add(right);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0) });
        Add(row);
        return row;
    }

    protected CheckBox Switch(string label, string id, bool value, Action<T, bool> set)
    {
        var box = new CheckBox { IsChecked = value };
        box.SetResourceReference(StyleProperty, "SwitchStyle");
        box.Checked += (_, _) => Changed(); box.Unchecked += (_, _) => Changed();
        OnSave(settings => set(settings, box.IsChecked == true));
        Row(label, id, box);
        return box;
    }

    protected ComboBox Pick<TValue>(string label, string id, IEnumerable<Choice<TValue>> choices, TValue value, Action<T, TValue> set)
    {
        var items = choices.ToArray();
        var box = new ComboBox { ItemsSource = items, SelectedItem = items.FirstOrDefault(item => Equals(item.Value, value)) ?? items[0], Width = 220 };
        box.SelectionChanged += (_, _) => Changed();
        OnSave(settings => { if (box.SelectedItem is Choice<TValue> choice) set(settings, choice.Value); });
        Row(label, id, box);
        return box;
    }

    protected Slider Slide(string label, string id, double low, double high, double step, double value, string unit, Action<T, double> set)
    {
        var slider = new Slider { Minimum = low, Maximum = high, TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 5, Width = 160, Value = value };
        var shown = new TextBlock { Width = 60, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void Show() => shown.Text = Math.Round(slider.Value, 2).ToString(CultureInfo.CurrentCulture) + unit;
        Show();
        slider.ValueChanged += (_, _) => { Show(); Changed(); };
        OnSave(settings => set(settings, slider.Value));
        Row(label, id, slider, shown);
        return slider;
    }

    /// <param name="whenDone">Saves when typing is finished instead of along the way.</param>
    protected TextBox Text(string label, string id, string value, Action<T, string> set, string hint = "", bool whenDone = false)
    {
        var box = new TextBox { Text = value, Width = 220, ToolTip = hint.Length > 0 ? "For example: " + hint : null };
        var done = value;
        if (!whenDone) box.TextChanged += (_, _) => Changed();
        else
        {
            void Finish() { if (done == box.Text.Trim()) return; done = box.Text.Trim(); Changed(); }
            box.LostKeyboardFocus += (_, _) => Finish();
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Finish(); };
        }
        OnSave(settings => set(settings, whenDone ? done : box.Text.Trim()));
        Row(label, id, box);
        return box;
    }

    protected TextBox Color(string label, string id, string value, Action<T, string> set, bool optional = false)
    {
        var box = new TextBox { Text = value, Width = 100, ToolTip = optional ? "Leave empty for automatic" : null };
        var swatch = new Button { Width = 34, Height = 34, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Choose a color" };
        var paint = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9) };
        swatch.Content = paint;
        AutomationProperties.SetName(swatch, "Choose " + label.ToLowerInvariant() + " color");
        void Show() => paint.Background = HexColor.TryParse(box.Text, out var color) ? new SolidColorBrush(color) : System.Windows.Media.Brushes.Transparent;
        Show();
        box.TextChanged += (_, _) => { Show(); Changed(); };
        swatch.Click += (_, _) =>
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
            if (HexColor.TryParse(box.Text, out var color)) dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
            if (Window.GetWindow(this) is not { } window) return;
            if (dialog.ShowDialog(new Owner(new WindowInteropHelper(window).Handle)) == System.Windows.Forms.DialogResult.OK)
                box.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        };
        OnSave(settings =>
        {
            var text = box.Text.Trim();
            set(settings, optional && text.Length == 0 || HexColor.TryParse(text, out _) ? text
                : throw new FormatException($"{label} must look like #DDF79D."));
        });
        Row(label, id, box, swatch);
        return box;
    }

    protected Button Press(string text, string id, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 8) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }
}
