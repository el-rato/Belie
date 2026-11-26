using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using WallpaperProfiles.Workspaces;
using Xunit;
using Xunit.Abstractions;

namespace WallpaperProfiles.Tests;

/// <summary>
/// Drives real windows. It docks a bar on the desktop for a few seconds, so it only runs when
/// BELIE_DESKTOP_TESTS is set to 1. Only windows of the helper process it starts are ever hidden.
/// </summary>
public sealed class TopBarDesktopTests
{
    private readonly ITestOutputHelper _output;
    public TopBarDesktopTests(ITestOutputHelper output) => _output = output;
    private static bool Enabled => Environment.GetEnvironmentVariable("BELIE_DESKTOP_TESTS") == "1";

    private const string Helper = """
        Add-Type -AssemblyName System.Windows.Forms
        $forms = 1..2 | ForEach-Object { $f = New-Object Windows.Forms.Form; $f.Text = "Belie test $_"; $f.StartPosition = 'Manual'; $f.Left = 200 * $_; $f.Top = 200; $f.Show(); $f }
        $forms[0].BackColor = 'SteelBlue'; $forms[1].BackColor = 'IndianRed'
        $d = New-Object Windows.Forms.Form; $d.Text = 'Belie dialog'; $d.ShowInTaskbar = $false; $d.Show($forms[0])
        [Windows.Forms.Application]::Run($forms[1])
        """;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint first, nint second);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint WindowFromPoint(System.Drawing.Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);

    private static Process PowerShell(string script)
    {
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-STA", "-EncodedCommand", Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    private static void Pump(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = time };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static bool Until(Func<bool> condition, int seconds = 10)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed.TotalSeconds > seconds) return false; Pump(TimeSpan.FromMilliseconds(50)); }
        return true;
    }

    private static void OnDesktop(Action<string> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "belie-desktop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SetThreadDpiAwarenessContext(-4); // Per-monitor, as Belie's manifest declares; the test runner's does not.
            Logger.Init(directory);
            try { test(directory); }
            catch (Exception ex) { failure = new Exception(string.Concat(Directory.GetFiles(directory, "*.log").Select(File.ReadAllText)), ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); Directory.Delete(directory, true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public void BarDocksAndWorkspacesHideAndRestoreRealWindows()
    {
        if (!Enabled) return;
        OnDesktop(directory =>
        {
            using var helper = PowerShell(Helper);
            try
            {
                var file = Path.Combine(directory, "topbar.json");
                new TopBarStore(file).Save(new TopBarSettings { Enabled = true, WorkspacesEnabled = true, Shortcuts = WorkspaceShortcutModifier.None, AnimationsEnabled = false });
                bool Mine(DesktopWindow window) => window.Identity.ProcessId == helper.Id;
                var system = new NativeWindowSystem(only: Mine);
                Assert.True(Until(() => system.VisibleWindows().Count >= 3), "The helper windows did not open.");
                var handles = system.VisibleWindows().Select(window => window.Handle).ToArray();
                var screen = System.Windows.Forms.Screen.PrimaryScreen!;
                var before = Work();
                using (var host = new TopBarHost(file, () => { }, Mine, guard: false))
                {
                    Assert.True(Until(() => host.Windows.Count > 0 && host.Workspaces?.States(screen.DeviceName)[0].WindowCount == 2),
                        $"Bars {host.Windows.Count} on {string.Join(",", host.Windows.Select(bar => bar.Monitor))}; expected {screen.DeviceName}; "
                        + string.Join("; ", system.VisibleWindows()));
                    Pump(TimeSpan.FromSeconds(2));
                    var docked = Work();
                    _output.WriteLine($"Work area {before} -> {docked}");
                    Assert.True(docked.Top > before.Top, "The bar did not reserve space.");
                    Assert.Equal(before.Bottom, docked.Bottom);
                    Screenshot(screen.Bounds, docked.Top - screen.Bounds.Top);
                    var manager = host.Workspaces!;
                    Assert.True(manager.Switch(screen.DeviceName, 2));
                    Assert.True(Until(() => handles.All(handle => !system.IsVisible(handle))), "Windows stayed visible.");
                    Pump(TimeSpan.FromSeconds(2)); // Outlast the settling time and several refreshes.
                    Assert.All(handles, handle => Assert.False(system.IsVisible(handle)));
                    Assert.Equal(2, manager.States(screen.DeviceName)[0].WindowCount);
                    Assert.True(manager.Switch(screen.DeviceName, 1));
                    Assert.True(Until(() => handles.All(system.IsVisible)), "Windows did not return.");
                    // Without the bar its space is given back and the workspaces go on.
                    var store = new TopBarStore(file);
                    void Change(Action<TopBarSettings> change) { var settings = store.Load(); change(settings); store.Save(settings); }
                    Change(settings => settings.Enabled = false);
                    Assert.True(Until(() => host.Windows.Count == 0 && Work() == before), "Turning the bar off left it, or its space, behind.");
                    Assert.Same(manager, host.Workspaces);
                    Assert.True(manager.Switch(screen.DeviceName, 2));
                    Assert.True(Until(() => handles.All(handle => !system.IsVisible(handle))), "Workspaces stopped with the bar.");
                    // And without workspaces their windows come back and the bar goes on.
                    Change(settings => { settings.Enabled = true; settings.WorkspacesEnabled = false; });
                    Assert.True(Until(() => host.Workspaces == null && host.Windows.Count > 0 && handles.All(system.IsVisible)), "Turning workspaces off left windows hidden.");
                    Change(settings => settings.WorkspacesEnabled = true);
                    Assert.True(Until(() => host.Workspaces?.States(screen.DeviceName)[0].WindowCount == 2), "Workspaces did not start again.");
                    manager = host.Workspaces!;
                    Assert.True(manager.Switch(screen.DeviceName, 3));
                    Assert.True(Until(() => handles.All(handle => !system.IsVisible(handle))));
                }
                Assert.True(Until(() => handles.All(system.IsVisible)), "Closing the bar left windows hidden.");
                Assert.True(Until(() => Work() == before), $"Closing the bar left space reserved: {Work()}");
            }
            finally { if (!helper.HasExited) helper.Kill(); }
        });
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public void WindowEventsArriveWithoutPolling()
    {
        if (!Enabled) return;
        OnDesktop(_ =>
        {
            var events = 0;
            using var watcher = new WindowEvents((_, _) => events++);
            using var helper = PowerShell(Helper);
            try { Assert.True(Until(() => events > 0), "No window events arrived."); }
            finally { if (!helper.HasExited) helper.Kill(); }
            _output.WriteLine($"Events: {events}");
        });
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public void RealWindowsSlideBetweenWorkspaces()
    {
        if (!Enabled) return;
        OnDesktop(directory =>
        {
            using var helper = PowerShell(Helper);
            try
            {
                var file = Path.Combine(directory, "topbar.json");
                var store = new TopBarStore(file);
                store.Save(new TopBarSettings { Enabled = true, WorkspacesEnabled = true, Shortcuts = WorkspaceShortcutModifier.None });
                bool Mine(DesktopWindow window) => window.Identity.ProcessId == helper.Id;
                var system = new NativeWindowSystem(only: Mine);
                Assert.True(Until(() => system.VisibleWindows().Count >= 3), "The helper windows did not open.");
                var apps = system.VisibleWindows().Where(window => window.Root == window.Handle).OrderBy(window => window.Title).Select(window => window.Handle).ToArray();
                var frames = new NativeWindowFrames();
                var monitor = System.Windows.Forms.Screen.PrimaryScreen!.DeviceName;
                System.Drawing.Rectangle[] before;
                using (var host = new TopBarHost(file, () => { }, Mine, guard: false))
                {
                    // Docking the bar makes Windows move some windows itself.
                    Pump(TimeSpan.FromSeconds(3));
                    before = apps.Select(frames.Frame).ToArray();
                    Assert.True(Until(() => host.Scene(monitor)?.IsRunning != true));
                    var manager = host.Workspaces!;
                    Assert.True(manager.Switch(monitor, 2));
                    // The red window (left 400) slides left, so its picture crosses the strip to its left while clicks still reach the real window.
                    var (moved, through) = (false, false);
                    var seen = new HashSet<string>();
                    var clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 450)
                    {
                        Pump(TimeSpan.FromMilliseconds(10));
                        using var row = new System.Drawing.Bitmap(360, 1);
                        using (var graphics = System.Drawing.Graphics.FromImage(row)) graphics.CopyFromScreen(20, 300, 0, 0, row.Size);
                        moved |= Enumerable.Range(0, row.Width).Any(x => row.GetPixel(x, 0).ToArgb() == System.Drawing.Color.IndianRed.ToArgb());
                        seen.Add(NativeMethods.ClassName(WindowFromPoint(new System.Drawing.Point(450, 300))));
                        NativeMethods.GetWindowThreadProcessId(WindowFromPoint(new System.Drawing.Point(450, 300)), out var under);
                        through |= host.Scene(monitor)!.IsRunning && under == (uint)helper.Id;
                    }
                    Assert.True(moved, "The slide was not drawn.");
                    Assert.True(through, "The animation swallowed clicks: " + string.Join(", ", seen));
                    Assert.True(Until(() => apps.All(handle => !system.IsVisible(handle))), "Windows stayed visible.");
                    Assert.True(Until(() => host.Scene(monitor)?.IsRunning != true));
                    _output.WriteLine($"Slid out in {host.Scene(monitor)!.FrameCount} frames");
                    Pump(TimeSpan.FromSeconds(2));
                    Assert.Equal(2, manager.States(monitor)[0].WindowCount);
                    Assert.True(manager.Switch(monitor, 1));
                    Frames("back", 6);
                    Assert.True(Until(() => apps.All(system.IsVisible) && host.Scene(monitor)?.IsRunning != true), "Windows did not return.");
                    _output.WriteLine($"Slid in with {host.Scene(monitor)!.FrameCount} frames");
                    // Faster than the animation.
                    foreach (var index in new[] { 2, 3, 1, 2, 1, 4, 1 }) { manager.Switch(monitor, index); Pump(TimeSpan.FromMilliseconds(120)); }
                    Assert.True(Until(() => host.Scene(monitor)?.IsRunning != true));
                    Pump(TimeSpan.FromSeconds(1.5));
                    Assert.All(apps, handle => Assert.True(system.IsVisible(handle)));
                    Assert.True(manager.Switch(monitor, 5));
                }
                Assert.True(Until(() => apps.All(system.IsVisible)), "Closing the bar left windows hidden.");
                Assert.True(Until(() => apps.Select(frames.Frame).SequenceEqual(before)),
                    $"Windows were not put back where they were: {string.Join(" ", before)} but now {string.Join(" ", apps.Select(frames.Frame))}");
            }
            finally { if (!helper.HasExited) helper.Kill(); }
        });
    }

    private static void Frames(string name, int count)
    {
        var export = Environment.GetEnvironmentVariable("BELIE_TOPBAR_FRAMES");
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        for (var i = 0; i < count; i++)
        {
            Pump(TimeSpan.FromMilliseconds(90));
            if (string.IsNullOrEmpty(export)) continue;
            using var full = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(full)) graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, full.Size);
            using var small = new System.Drawing.Bitmap(full, 96, 54);
            small.Save(Path.Combine(export, $"{name}-{i}.png"));
        }
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public void GuardRestoresWindowsAfterTheHostIsKilled()
    {
        if (!Enabled) return;
        OnDesktop(directory =>
        {
            using var helper = PowerShell(Helper);
            var file = Path.Combine(directory, "topbar.json");
            // Stands in for the host: holds its mutex until it is killed.
            using var host = PowerShell($"$m = New-Object Threading.Mutex($true, '{TopBarProcess.MutexName(file)}'); $c = New-Object Threading.EventWaitHandle($false, 'ManualReset', '{TopBarProcess.ClosedEventName(file)}'); Start-Sleep 120; $m.ReleaseMutex()");
            try
            {
                var system = new NativeWindowSystem(only: window => window.Identity.ProcessId == helper.Id);
                Assert.True(Until(() => system.VisibleWindows().Count >= 3 && TopBarProcess.IsRunning(file)));
                var windows = system.VisibleWindows();
                var exe = Path.Combine(AppContext.BaseDirectory, "Belie.exe");
                using var guard = Process.Start(new ProcessStartInfo(exe, $"{TopBarProcess.GuardArgument} {host.Id} \"{file}\"") { UseShellExecute = false })!;
                Pump(TimeSpan.FromSeconds(2));
                Assert.False(guard.HasExited, "The guard did not wait for the host.");
                guard.Refresh();
                _output.WriteLine($"Guard memory: {guard.PrivateMemorySize64 / 1048576} MB private");
                new HiddenWindowJournal(HiddenWindowJournal.PathFor(file)).Save(windows.Select(window => window.Identity).ToArray());
                foreach (var window in windows) system.Hide(window.Handle);
                Assert.True(Until(() => windows.All(window => !system.IsVisible(window.Handle))));
                host.Kill();
                Assert.True(Until(() => windows.All(window => system.IsVisible(window.Handle))), "The guard left windows hidden.");
                Assert.True(guard.WaitForExit(5000));
            }
            finally
            {
                if (!host.HasExited) host.Kill();
                if (!helper.HasExited) helper.Kill();
            }
        });
    }

    private static System.Drawing.Rectangle Work()
    {
        var info = new NativeMethods.MONITORINFOEX { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(), Device = "" };
        NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(NativeMethods.GetShellWindow(), 1), ref info);
        return System.Drawing.Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    private static void Screenshot(System.Drawing.Rectangle screen, int height)
    {
        var export = Environment.GetEnvironmentVariable("BELIE_TOPBAR_SCREENSHOT");
        if (string.IsNullOrEmpty(export)) return;
        // Only the strip the bar occupies.
        using var bitmap = new System.Drawing.Bitmap(screen.Width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(screen.X, screen.Y, 0, 0, bitmap.Size);
        bitmap.Save(export);
    }
}
