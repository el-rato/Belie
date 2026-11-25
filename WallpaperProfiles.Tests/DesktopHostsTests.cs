using System.IO;
using System.Windows.Threading;
using WallpaperProfiles.Engine;
using WallpaperProfiles.Models;
using WallpaperProfiles.Persistence;
using Xunit;

namespace WallpaperProfiles.Tests;

public sealed class DesktopHostsTests
{
    private sealed class FakeHost : IDisposable
    {
        private readonly Mutex _mutex;
        public readonly Action Stop;
        public bool Disposed;
        public FakeHost(string mutex, Action stop) { _mutex = new Mutex(true, mutex); Stop = stop; }
        public void Dispose() { Disposed = true; _mutex.ReleaseMutex(); _mutex.Dispose(); }
    }

    [Fact]
    public void WidgetsAndBarShareOneProcessAndComeAndGoSeparately()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "belie-hosts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { Check(directory); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); Directory.Delete(directory, true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void Check(string directory)
    {
        var canvasFile = Path.Combine(directory, Path.GetFileName(DesktopCanvasProcess.DefaultFile));
        var barFile = DesktopHosts.BarFileFor(canvasFile);
        Assert.Equal(canvasFile, DesktopHosts.CanvasFileFor(barFile));
        Assert.False(DesktopHosts.RequestAttach(canvasFile, barFile)); // Nothing runs yet, so a process must be started.
        var bars = new TopBarStore(barFile);
        bars.Save(new TopBarSettings { Enabled = true });
        FakeHost? canvas = null, bar = null;
        var exits = 0;
        using var hosts = new DesktopHosts(canvasFile, barFile, () => exits++,
            (file, stop) => canvas = new FakeHost(DesktopCanvasProcess.MutexName(file), stop),
            (file, stop) => bar = new FakeHost(TopBarProcess.MutexName(file), stop));
        Assert.True(hosts.HasBar); Assert.False(hosts.HasCanvas);

        var extraExits = 0;
        using (var extra = new DesktopHosts(canvasFile, barFile, () => extraExits++, (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException()))
            Assert.False(extra.HasBar || extra.HasCanvas);
        Assert.Equal(1, extraExits);

        new DesktopCanvasStore(canvasFile).Update(document => { document.Enabled = true; document.Widgets.Add(new DesktopWidget { Enabled = true }); });
        Assert.True(DesktopCanvasProcess.IsWanted(canvasFile));
        DesktopCanvasProcess.Start(canvasFile); // Joins this process instead of starting another.
        Assert.True(Until(() => hosts.HasCanvas));

        var firstBar = bar!;
        bars.Save(new TopBarSettings { Enabled = false });
        firstBar.Stop();
        Assert.True(firstBar.Disposed); Assert.False(hosts.HasBar); Assert.True(hosts.HasCanvas);
        Assert.Equal(0, exits); // The widgets still need the process.
        firstBar.Stop(); // A late second request changes nothing.
        bars.Save(new TopBarSettings { Enabled = true });
        TopBarProcess.Start(barFile);
        Assert.True(Until(() => hosts.HasBar));
        Assert.NotSame(firstBar, bar);

        bar!.Stop(); canvas!.Stop();
        Assert.Equal(1, exits);
    }

    private static bool Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
        return condition();
    }
}
