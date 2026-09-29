using System.Globalization;
using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Triggers;

internal sealed class IdleTriggerMonitor : TriggerMonitorBase
{
    private readonly DispatcherTimer _timer;
    private List<(Guid ProfileId, EventTrigger Trigger)> _triggers = new();

    public IdleTriggerMonitor(Action<Guid, EventTrigger, bool> raise) : base(raise)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Evaluate();
    }

    public override void Reload(IReadOnlyList<WallpaperProfile> profiles)
    {
        _triggers = profiles
            .SelectMany(p => p.EventTriggers.Where(t => t.Type == TriggerType.IdleForMinutes).Select(t => (p.Id, t)))
            .ToList();
        ResetStates();
        _timer.IsEnabled = _triggers.Count > 0;
        if (_triggers.Count > 0)
        {
            Evaluate();
        }
    }

    private void Evaluate()
    {
        if (_triggers.Count == 0)
        {
            return;
        }
        var idle = NativeMethods.GetIdleTime();
        foreach (var (profileId, trigger) in _triggers)
        {
            var minutes = double.TryParse(trigger.Condition, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 0;
            Report(profileId, trigger, idle.TotalMinutes >= minutes && minutes > 0);
        }
    }

    public override void Dispose() => _timer.Stop();
}
