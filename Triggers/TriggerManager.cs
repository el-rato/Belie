using WallpaperProfiles.Models;

namespace WallpaperProfiles.Triggers;

internal sealed class TriggerManager : IDisposable
{
    public event Action<Guid, EventTrigger, bool>? TriggerStateChanged;

    private readonly BatteryTriggerMonitor _battery;
    private readonly IdleTriggerMonitor _idle;
    private readonly ProcessTriggerMonitor _process;

    public TriggerManager()
    {
        _battery = new BatteryTriggerMonitor(OnTrigger);
        _idle = new IdleTriggerMonitor(OnTrigger);
        _process = new ProcessTriggerMonitor(OnTrigger);
    }

    private void OnTrigger(Guid profileId, EventTrigger trigger, bool active)
        => TriggerStateChanged?.Invoke(profileId, trigger, active);

    public void Reload(IReadOnlyList<WallpaperProfile> profiles)
    {
        _battery.Reload(profiles);
        _idle.Reload(profiles);
        _process.Reload(profiles);
    }

    public void Dispose()
    {
        _battery.Dispose();
        _idle.Dispose();
        _process.Dispose();
    }
}
