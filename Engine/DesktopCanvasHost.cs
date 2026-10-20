using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.UI;

namespace WallpaperProfiles.Engine;

internal sealed class DesktopCanvasHost : IDisposable
{
    private readonly DesktopCanvasStore _store;
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _stop;
    private readonly EventWaitHandle _changed;
    private readonly RegisteredWaitHandle _changeWait;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, DesktopWidgetWindow> _windows = new();
    private readonly Func<DesktopWidget, DesktopWidgetWindow> _createWindow;
    private bool _disposed;
    private readonly HashSet<Guid> _linksRefreshing = new();
    private readonly Dictionary<Guid, DateTimeOffset> _linkAttempts = new();
    private readonly Dictionary<Guid, string> _linkSources = new();

    public DesktopCanvasHost(string file, Func<DesktopWidget, DesktopWidgetWindow>? createWindow = null)
    {
        _store = new DesktopCanvasStore(file);
        _mutex = new Mutex(true, DesktopCanvasProcess.MutexName(file), out var first);
        if (!first) { _mutex.Dispose(); throw new InvalidOperationException("The desktop canvas is already running."); }
        _stop = new EventWaitHandle(false, EventResetMode.AutoReset, DesktopCanvasProcess.MutexName(file) + ".Stop");
        _createWindow = createWindow ?? (widget => new DesktopWidgetWindow(widget, _store));
        _changed = new EventWaitHandle(false, EventResetMode.AutoReset, DesktopCanvasProcess.MutexName(file) + ".Changed");
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        _changeWait = ThreadPool.RegisterWaitForSingleObject(_changed, (_, _) =>
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(Refresh);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        System.Windows.Application.Current.Dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            if (_stop.WaitOne(0)) { System.Windows.Application.Current.Shutdown(); return; }
            var document = _store.Load();
            if (!document.Enabled || !document.Widgets.Any(w => w.Enabled))
            { System.Windows.Application.Current.Shutdown(); return; }
            // Stay alive when the current profile has no widgets; the next switch can reveal them.
            var enabled = document.VisibleWidgets().ToDictionary(w => w.Id);
            foreach (var id in _windows.Keys.Where(id => !enabled.ContainsKey(id)).ToArray())
            {
                var window = _windows[id];
                _windows.Remove(id);
                window.Close();
            }
            foreach (var widget in enabled.Values)
            {
                if (!_windows.TryGetValue(widget.Id, out var window))
                {
                    window = _createWindow(widget);
                    _windows.Add(widget.Id, window);
                    window.Closed += (_, _) => _windows.Remove(widget.Id);
                    window.Show();
                }
                else window.Update(widget);
                window.RefreshCountdown();
                window.EnsureDesktop();
                if (widget.Kind == DesktopWidgetKind.Link && !_linksRefreshing.Contains(widget.Id)
                    && (!_linkSources.TryGetValue(widget.Id, out var source) || source != widget.Content + "|" + widget.LeetCodeUsername
                        || !_linkAttempts.TryGetValue(widget.Id, out var attempted) || DateTimeOffset.UtcNow - attempted > TimeSpan.FromMinutes(5))
                    && (!widget.LinkUpdatedAt.HasValue || DateTimeOffset.UtcNow - widget.LinkUpdatedAt > TimeSpan.FromMinutes(15)
                        || widget.IsLeetCode && widget.PotdDate != DateOnly.FromDateTime(DateTime.UtcNow)))
                {
                    _linksRefreshing.Add(widget.Id); _linkAttempts[widget.Id] = DateTimeOffset.UtcNow;
                    _linkSources[widget.Id] = widget.Content + "|" + widget.LeetCodeUsername;
                    RefreshLink(widget);
                }
            }
        }
        catch (Exception ex) { Logger.Error("Refreshing desktop widgets failed; the saved canvas was preserved.", ex); }
    }

    private async void RefreshLink(DesktopWidget widget)
    {
        try { await Task.Run(() => DesktopLinkService.RefreshAsync(_store, widget)); }
        catch (Exception ex) { Logger.Error("Refreshing link card failed.", ex); }
        finally { _linksRefreshing.Remove(widget.Id); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _changeWait.Unregister(null);
        _changed.Dispose();
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
