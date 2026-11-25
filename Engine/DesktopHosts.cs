using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;

namespace WallpaperProfiles.Engine;

/// <summary>
/// Runs the desktop widgets and the top bar in one process, because each extra process costs about 60 MB.
/// Either can stop or start without the other.
/// </summary>
internal sealed class DesktopHosts : IDisposable
{
    private readonly string _canvasFile, _barFile;
    private readonly Action _exit;
    private readonly EventWaitHandle _attach;
    private readonly RegisteredWaitHandle _attachWait;
    private readonly Func<string, Action, IDisposable> _createCanvas, _createBar;
    private IDisposable? _canvas, _bar;
    private bool _disposed, _starting;

    internal bool HasCanvas => _canvas != null;
    internal bool HasBar => _bar != null;

    public DesktopHosts(string canvasFile, string barFile, Action exit,
        Func<string, Action, IDisposable>? createCanvas = null, Func<string, Action, IDisposable>? createBar = null)
    {
        _canvasFile = Path.GetFullPath(canvasFile);
        _barFile = Path.GetFullPath(barFile);
        _exit = exit;
        _createCanvas = createCanvas ?? ((file, stopped) => new DesktopCanvasHost(file, exit: stopped));
        _createBar = createBar ?? ((file, stopped) => new TopBarHost(file, stopped));
        _attach = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(_canvasFile, _barFile), out var first);
        var dispatcher = Dispatcher.CurrentDispatcher;
        _attachWait = ThreadPool.RegisterWaitForSingleObject(_attach, (_, _) =>
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(Attach);
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        // Widgets and bar are asked for separately, so two processes can start at once. The first one takes both.
        if (first) Attach();
        else { _attach.Set(); Dispose(); exit(); }
    }

    private static string EventName(string canvasFile, string barFile) => @"Local\Belie.Hosts." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes((Path.GetFullPath(canvasFile) + "|" + Path.GetFullPath(barFile)).ToUpperInvariant())))[..16];

    public static string CanvasFileFor(string barFile) => Beside(barFile, DesktopCanvasProcess.DefaultFile);
    public static string BarFileFor(string canvasFile) => Beside(canvasFile, TopBarProcess.DefaultFile);
    private static string Beside(string file, string other) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFileName(other));

    /// <summary>Asks a running host to start whatever is newly enabled. False when there is no such host.</summary>
    public static bool RequestAttach(string canvasFile, string barFile)
    {
        if (!EventWaitHandle.TryOpenExisting(EventName(canvasFile, barFile), out var attach)) return false;
        using (attach) attach.Set();
        return true;
    }

    private void Attach()
    {
        if (_disposed) return;
        _starting = true;
        if (_canvas == null) _canvas = Start("desktop widgets", () => DesktopCanvasProcess.IsWanted(_canvasFile) && !DesktopCanvasProcess.IsRunning(_canvasFile)
            ? _createCanvas(_canvasFile, () => Stopped(ref _canvas)) : null);
        if (_bar == null) _bar = Start("the top bar", () => TopBarProcess.IsWanted(_barFile) && !TopBarProcess.IsRunning(_barFile)
            ? _createBar(_barFile, () => Stopped(ref _bar)) : null);
        _starting = false;
        ExitWhenIdle();
    }

    private static IDisposable? Start(string name, Func<IDisposable?> create)
    {
        try { return create(); }
        catch (Exception ex) { Logger.Error($"Starting {name} failed.", ex); return null; }
    }

    private void Stopped(ref IDisposable? host)
    {
        if (host is not { } stopped) return;
        host = null;
        try { stopped.Dispose(); }
        catch (Exception ex) { Logger.Error("Stopping part of the desktop host failed.", ex); }
        ExitWhenIdle();
    }

    private void ExitWhenIdle() { if (!_starting && !_disposed && _canvas == null && _bar == null) _exit(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _attachWait.Unregister(null);
        _attach.Dispose();
        _bar?.Dispose(); // First, so windows on other workspaces come back.
        _bar = null;
        _canvas?.Dispose();
        _canvas = null;
    }
}
