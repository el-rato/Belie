using System.Windows.Threading;

namespace WallpaperProfiles.Infrastructure;

internal static class MemoryTrim
{
    /// <summary>Returns memory that was only needed to start or close something, once things are quiet.</summary>
    public static void Soon()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        };
        timer.Start();
    }
}
