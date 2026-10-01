using WinForms = System.Windows.Forms;
using WallpaperProfiles.Coordination;

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

    private void RebuildMenu()
    {
        var snap = _snapshot();
        _menu?.Dispose();
        _menu = new WinForms.ContextMenuStrip();

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

        AddItem("Manage Profiles…", _openMain);

        var pause = new WinForms.ToolStripMenuItem("Pause Scheduling") { Checked = snap.Paused };
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
