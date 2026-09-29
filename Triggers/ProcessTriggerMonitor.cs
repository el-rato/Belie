using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Triggers;

internal sealed class ProcessTriggerMonitor : TriggerMonitorBase
{
    private sealed class WatchEntry
    {
        public ManagementEventWatcher? Watcher;
        public HashSet<int> RunningPids { get; } = new();
        public List<(Guid ProfileId, EventTrigger Trigger)> Triggers { get; } = new();
    }

    private readonly Dictionary<string, WatchEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _exitPoller;
    private bool _disposed;

    public ProcessTriggerMonitor(Action<Guid, EventTrigger, bool> raise) : base(raise)
    {
        _exitPoller = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _exitPoller.Tick += (_, _) => PollForExitedProcesses();
    }

    public override void Reload(IReadOnlyList<WallpaperProfile> profiles)
    {
        Teardown();
        foreach (var profile in profiles)
        {
            foreach (var trigger in profile.EventTriggers.Where(t => t.Type == TriggerType.ProcessLaunch))
            {
                var name = NormalizeProcessName(trigger.Condition);
                if (name.Length == 0)
                {
                    Logger.Warn($"Ignoring process-launch trigger without a condition (profile '{profile.Name}').");
                    continue;
                }
                if (!_entries.TryGetValue(name, out var entry))
                {
                    entry = new WatchEntry();
                    _entries[name] = entry;
                }
                entry.Triggers.Add((profile.Id, trigger));
            }
        }

        foreach (var (name, entry) in _entries)
        {
            SeedAlreadyRunning(name, entry);
            StartWatcher(name, entry);
            if (entry.RunningPids.Count > 0)
            {
                Logger.Info($"Process trigger armed for '{name}' (already running).");
                foreach (var (profileId, trigger) in entry.Triggers)
                {
                    Report(profileId, trigger, true);
                }
            }
            else
            {
                Logger.Info($"Process trigger armed for '{name}'.");
            }
        }
        _exitPoller.IsEnabled = _entries.Count > 0;
    }

    private static void SeedAlreadyRunning(string name, WatchEntry entry)
    {
        try
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            foreach (var process in Process.GetProcessesByName(stem))
            {
                entry.RunningPids.Add(process.Id);
                process.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not seed running processes for '{name}': {ex.Message}");
        }
    }

    private void StartWatcher(string name, WatchEntry entry)
    {
        try
        {
            var query = new WqlEventQuery(
                "__InstanceCreationEvent",
                TimeSpan.FromSeconds(2),
                $"TargetInstance ISA 'Win32_Process' AND TargetInstance.Name = '{SanitizeForWql(name)}'");
            entry.Watcher = new ManagementEventWatcher(query);
            entry.Watcher.EventArrived += (_, args) =>
            {
                try
                {
                    var target = (ManagementBaseObject?)args.NewEvent["TargetInstance"];
                    var pid = Convert.ToInt32(target?["ProcessId"], CultureInfo.InvariantCulture);
                    Ui.BeginInvoke(new Action(() => OnProcessStarted(name, pid)));
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not parse process start event: {ex.Message}");
                }
            };
            entry.Watcher.Start();
        }
        catch (Exception ex)
        {
            Logger.Error($"WMI watcher could not start for '{name}' (launch events inactive).", ex);
        }
    }

    private void OnProcessStarted(string name, int pid)
    {
        if (_disposed || !_entries.TryGetValue(name, out var entry))
        {
            return;
        }
        if (!entry.RunningPids.Add(pid))
        {
            return;
        }
        if (entry.RunningPids.Count != 1)
        {
            return;
        }
        foreach (var (profileId, trigger) in entry.Triggers)
        {
            Report(profileId, trigger, true);
        }
    }

    private void PollForExitedProcesses()
    {
        foreach (var (_, entry) in _entries)
        {
            if (entry.RunningPids.Count == 0)
            {
                continue;
            }
            var gone = entry.RunningPids.Where(pid => !IsAlive(pid)).ToList();
            if (gone.Count == 0)
            {
                continue;
            }
            foreach (var pid in gone)
            {
                entry.RunningPids.Remove(pid);
            }
            if (entry.RunningPids.Count == 0)
            {
                foreach (var (profileId, trigger) in entry.Triggers)
                {
                    Report(profileId, trigger, false);
                }
            }
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static string NormalizeProcessName(string condition)
    {
        var name = condition.Trim().Trim('"');
        var slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }
        name = name.Trim().ToLowerInvariant();
        if (name.Length > 0 && !name.EndsWith(".exe", StringComparison.Ordinal))
        {
            name += ".exe";
        }
        return name;
    }

    private static string SanitizeForWql(string name) => Regex.Replace(name, @"[^a-z0-9._\-]", "");

    private void Teardown()
    {
        _exitPoller.Stop();
        foreach (var entry in _entries.Values)
        {
            try
            {
                entry.Watcher?.Stop();
            }
            catch
            {
            }
            entry.Watcher?.Dispose();
            entry.Watcher = null;
        }
        _entries.Clear();
        ResetStates();
    }

    public override void Dispose()
    {
        _disposed = true;
        Teardown();
    }
}
