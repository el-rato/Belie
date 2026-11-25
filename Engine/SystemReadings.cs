using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using WallpaperProfiles.Infrastructure;
using WinForms = System.Windows.Forms;

namespace WallpaperProfiles.Engine;

/// <param name="Down">Bytes received each second.</param>
/// <param name="Up">Bytes sent each second.</param>
internal sealed record SystemSnapshot(int Cpu, int Memory, int? Volume, bool Muted, int? Battery, bool Charging,
    int Disk = 0, double Down = 0, double Up = 0, TimeSpan Uptime = default)
{
    public static string Speed(double bytes) => bytes >= 1_000_000 ? $"{bytes / 1_000_000:0.0} MB/s" : $"{bytes / 1000:0} KB/s";
    public static string Duration(TimeSpan time) => time.TotalDays >= 1 ? $"{(int)time.TotalDays}d {time.Hours}h" : time.TotalHours >= 1 ? $"{time.Hours}h {time.Minutes}m" : $"{time.Minutes}m";
}

/// <summary>Processor, memory, disk, network, volume and battery readings for the bar and the system widget.</summary>
internal sealed class SystemReadings : IDisposable
{
    private long _idle, _total, _received, _sent, _networkRead;

    public SystemSnapshot Read(bool disk = false, bool network = false)
    {
        var (volume, muted) = ReadVolume();
        var power = WinForms.SystemInformation.PowerStatus;
        int? battery = (power.BatteryChargeStatus & (WinForms.BatteryChargeStatus.NoSystemBattery | WinForms.BatteryChargeStatus.Unknown)) != 0
            || power.BatteryLifePercent is < 0 or > 1 ? null : (int)Math.Round(power.BatteryLifePercent * 100);
        var (down, up) = network ? ReadNetwork() : (0, 0);
        return new SystemSnapshot(ReadCpu(), ReadMemory(), volume, muted, battery, power.PowerLineStatus == WinForms.PowerLineStatus.Online,
            disk ? ReadDisk() : 0, down, up, TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    private int ReadCpu()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var result = Percent(idle - _idle, kernel + user - _total); // Kernel time includes idle time.
        var first = _total == 0;
        _idle = idle; _total = kernel + user;
        return first ? 0 : result;
    }

    internal static int Percent(long idle, long total) => total <= 0 ? 0 : (int)Math.Clamp(Math.Round(100d * (total - idle) / total), 0, 100);

    private static int ReadMemory()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status) ? (int)status.Load : 0;
    }

    private static int ReadDisk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            return Percent(drive.TotalFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private (double Down, double Up) ReadNetwork()
    {
        long received = 0, sent = 0;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Virtual adapters have no gateway and would count the same traffic again.
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || adapter.GetIPProperties().GatewayAddresses.Count == 0) continue;
                var totals = adapter.GetIPStatistics();
                received += totals.BytesReceived; sent += totals.BytesSent;
            }
        }
        catch (NetworkInformationException) { return (0, 0); } // An adapter was just removed.
        var now = Environment.TickCount64;
        var seconds = (now - _networkRead) / 1000d;
        // Totals fall when an adapter goes away.
        var result = _networkRead == 0 || seconds <= 0 ? (0, 0) : (Math.Max(0, received - _received) / seconds, Math.Max(0, sent - _sent) / seconds);
        // Totals rise at once when an adapter comes back; that is not traffic.
        if (_received == 0 || received < _received) result = (0, 0);
        _received = received; _sent = sent; _networkRead = now;
        return result;
    }

    private (int? Volume, bool Muted) ReadVolume()
    {
        (int?, bool) result = (null, false);
        WithVolume(volume => result = ((int)Math.Round(volume.MasterVolumeLevelScalar * 100), volume.Mute));
        return result;
    }

    public void ChangeVolume(int percent) => WithVolume(volume =>
        volume.MasterVolumeLevelScalar = Math.Clamp(volume.MasterVolumeLevelScalar + percent / 100f, 0, 1));

    public void ToggleMute() => WithVolume(volume => volume.Mute = !volume.Mute);

    private void WithVolume(Action<AudioEndpointVolume> action)
    {
        try
        {
            // Read on one thread and changed on another, which one enumerator does not allow.
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            action(device.AudioEndpointVolume);
        }
        catch (COMException) { } // No playback device, or it was just removed.
    }

    public void Dispose() { }
}
