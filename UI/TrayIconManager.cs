using System.Drawing;
using System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;
using WallpaperProfiles.Coordination;
using WallpaperProfiles.Infrastructure;

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
            Icon = LoadAppIcon(),
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

    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (extracted != null)
                {
                    return extracted;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not extract app icon for tray: {ex.Message}");
        }
        return CreateIcon();
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Rectangle(0, 0, 32, 32),
                System.Drawing.Color.FromArgb(68, 138, 255),
                System.Drawing.Color.FromArgb(140, 82, 255),
                45f);
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2f);
            g.DrawRectangle(pen, 8, 11, 16, 12);
            g.DrawLine(pen, 12, 8, 12, 11);
            g.DrawLine(pen, 20, 8, 20, 11);
        }
        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _menu?.Dispose();
        var handle = _icon.Icon?.Handle ?? IntPtr.Zero;
        _icon.Icon = null;
        _icon.Dispose();
        if (handle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
