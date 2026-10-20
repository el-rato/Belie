using WinForms = System.Windows.Forms;
using WallpaperProfiles.Coordination;
using Drawing = System.Drawing;

namespace WallpaperProfiles.UI;

internal sealed class TrayIconManager : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Func<WallpaperCoordinator.Snapshot> _snapshot;
    private readonly Action _openMain;
    private readonly Action _togglePause;
    private readonly Action _exitApp;
    private readonly Action<Guid> _switchTo;
    private WinForms.ContextMenuStrip? _menu;

    public TrayIconManager(Func<WallpaperCoordinator.Snapshot> snapshot, Action openMain,
        Action<Guid> switchTo, Action togglePause, Action exitApp)
    {
        _snapshot = snapshot;
        _openMain = openMain;
        _switchTo = switchTo;
        _togglePause = togglePause;
        _exitApp = exitApp;

        _icon = new WinForms.NotifyIcon
        {
            Icon = UiAppearance.CreateTrayIcon(),
            Text = "Belie",
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _openMain();
        _icon.BalloonTipClicked += (_, _) => _openMain();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                _openMain();
            }
        };
        RebuildMenu();
        UiAppearance.Changed += UpdateThemeIcon;
    }

    public void Refresh() => RebuildMenu();

    public void ShowUpdateAvailable() => _icon.ShowBalloonTip(8000, "Belie update available",
        "Open Belie to download the update and restart.", WinForms.ToolTipIcon.Info);

    private void RebuildMenu()
    {
        var snap = _snapshot();
        _menu?.Dispose();
        _menu = CreateThemedMenu();

        foreach (var profile in snap.Profiles)
        {
            var id = profile.Id;
            var item = new WinForms.ToolStripMenuItem(profile.Name) { Checked = snap.ActiveProfileId == id };
            item.Click += (_, _) => _switchTo(id);
            _menu.Items.Add(item);
        }

        if (snap.Profiles.Count > 0)
        {
            _menu.Items.Add(new WinForms.ToolStripSeparator());
        }

        AddItem("Open Belie", _openMain);

        var pause = new WinForms.ToolStripMenuItem("Pause automation") { Checked = snap.Paused };
        pause.Click += (_, _) => _togglePause();
        _menu.Items.Add(pause);

        _menu.Items.Add(new WinForms.ToolStripSeparator());
        AddItem("Exit", _exitApp);

        _icon.ContextMenuStrip = _menu;
        _icon.Text = snap.Summary.Length <= 63 ? snap.Summary : snap.Summary[..63];
    }

    private void AddItem(string text, Action onClick)
    {
        var item = new WinForms.ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        _menu!.Items.Add(item);
    }

    private void UpdateThemeIcon()
    {
        var previous = _icon.Icon;
        _icon.Icon = UiAppearance.CreateTrayIcon();
        previous?.Dispose();
        if (_menu != null) ApplyMenuTheme(_menu);
    }

    internal static WinForms.ContextMenuStrip CreateThemedMenu()
    {
        var font = new Drawing.Font("Segoe UI", 10.5f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);
        var menu = new WinForms.ContextMenuStrip
        {
            Font = font, ShowImageMargin = false, ShowCheckMargin = true,
            Padding = new WinForms.Padding(6), MinimumSize = new Drawing.Size(224, 0)
        };
        menu.ItemAdded += (_, e) =>
        {
            if (e.Item is WinForms.ToolStripMenuItem) e.Item.Padding = new WinForms.Padding(4, 10, 12, 10);
        };
        menu.Disposed += (_, _) => font.Dispose();
        ApplyMenuTheme(menu);
        return menu;
    }

    private static Drawing.Color MenuColor(string key, string fallback)
    {
        // The native tray menu shares WPF's live palette, including scene accent overrides.
        if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush brush)
            return Drawing.Color.FromArgb(brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B);
        return Drawing.ColorTranslator.FromHtml(fallback);
    }

    private static void ApplyMenuTheme(WinForms.ContextMenuStrip menu)
    {
        var theme = UiAppearance.Current;
        menu.BackColor = MenuColor("SurfaceBrush", theme.Surface);
        menu.ForeColor = MenuColor("TextPrimaryBrush", theme.Text);
        menu.Renderer = WinForms.SystemInformation.HighContrast ? new WinForms.ToolStripSystemRenderer()
            : new TrayMenuRenderer(menu.BackColor, menu.ForeColor,
                MenuColor("SurfaceHoverBrush", theme.Hover), MenuColor("BorderBrush", theme.Border),
                MenuColor("AccentTextBrush", theme.Accent), MenuColor("TextTertiaryBrush", theme.Muted));
        menu.Invalidate();
    }

    private sealed class TrayMenuRenderer(Drawing.Color background, Drawing.Color text, Drawing.Color hover,
        Drawing.Color border, Drawing.Color accent, Drawing.Color muted)
        : WinForms.ToolStripProfessionalRenderer(new MenuColors(background, hover, border))
    {
        protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
        {
            using var fill = new Drawing.SolidBrush(e.Item.Selected && e.Item.Enabled ? hover : background);
            e.Graphics.FillRectangle(fill, new Drawing.Rectangle(Drawing.Point.Empty, e.Item.Size));
        }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? muted : e.Item is WinForms.ToolStripMenuItem { Checked: true } ? accent : text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            var box = e.ImageRectangle;
            using var pen = new Drawing.Pen(accent, Math.Max(1.5f, box.Height / 9f));
            var smoothing = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new Drawing.PointF(box.Left + box.Width * .2f, box.Top + box.Height * .5f),
                new Drawing.PointF(box.Left + box.Width * .42f, box.Top + box.Height * .72f),
                new Drawing.PointF(box.Left + box.Width * .8f, box.Top + box.Height * .25f)
            });
            e.Graphics.SmoothingMode = smoothing;
        }

        protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(border);
            e.Graphics.DrawLine(pen, 6, e.Item.Height / 2, e.Item.Width - 6, e.Item.Height / 2);
        }
    }

    private sealed class MenuColors(Drawing.Color background, Drawing.Color hover, Drawing.Color border)
        : WinForms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => background;
        public override Drawing.Color ImageMarginGradientBegin => background;
        public override Drawing.Color ImageMarginGradientMiddle => background;
        public override Drawing.Color ImageMarginGradientEnd => background;
        public override Drawing.Color MenuBorder => border;
        public override Drawing.Color MenuItemBorder => hover;
    }

    public void Dispose()
    {
        UiAppearance.Changed -= UpdateThemeIcon;
        _icon.Visible = false;
        _menu?.Dispose();
        var image = _icon.Icon;
        _icon.Icon = null;
        _icon.Dispose();
        image?.Dispose();
    }
}
