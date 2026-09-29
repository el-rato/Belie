using System.Globalization;
using System.Windows.Threading;
using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;
using WinForms = System.Windows.Forms;

namespace WallpaperProfiles.Triggers;

internal sealed class BatteryTriggerMonitor : TriggerMonitorBase
{
    private readonly DispatcherTimer _timer;
    private List<(Guid ProfileId, EventTrigger Trigger)> _triggers = new();

    public BatteryTriggerMonitor(Action<Guid, EventTrigger, bool> raise) : base(raise)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Evaluate();
    }

    public override void Reload(IReadOnlyList<WallpaperProfile> profiles)
    {
        _triggers = profiles
            .SelectMany(p => p.EventTriggers.Where(IsBatteryTrigger).Select(t => (p.Id, t)))
            .ToList();
        ResetStates();
        _timer.IsEnabled = _triggers.Count > 0;
        if (_triggers.Count > 0)
        {
            Evaluate();
        }
    }

    private static bool IsBatteryTrigger(EventTrigger trigger)
        => trigger.Type is TriggerType.OnBattery or TriggerType.Charging or TriggerType.BelowBatteryPercent;

    private void Evaluate()
    {
        if (_triggers.Count == 0)
        {
            return;
        }
        var status = WinForms.SystemInformation.PowerStatus;
        var chargeStatus = status.BatteryChargeStatus;
        var hasBattery = chargeStatus != WinForms.BatteryChargeStatus.NoSystemBattery;
        var knownLevel = hasBattery && !chargeStatus.HasFlag(WinForms.BatteryChargeStatus.Unknown);

        foreach (var (profileId, trigger) in _triggers)
        {
            var active = trigger.Type switch
            {
                TriggerType.OnBattery => hasBattery && status.PowerLineStatus == WinForms.PowerLineStatus.Offline,
                TriggerType.Charging => hasBattery && status.PowerLineStatus == WinForms.PowerLineStatus.Online,
                TriggerType.BelowBatteryPercent => knownLevel
                    && float.TryParse(trigger.Condition, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
                    && status.BatteryLifePercent * 100f <= percent,
                _ => false,
            };
            Report(profileId, trigger, active);
        }
    }

    public override void Dispose() => _timer.Stop();
}
