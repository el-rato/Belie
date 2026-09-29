using System.Windows.Threading;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Triggers;

internal abstract class TriggerMonitorBase : IDisposable
{
    protected Action<Guid, EventTrigger, bool> Raise { get; }

    protected Dispatcher Ui { get; }

    private readonly Dictionary<(Guid ProfileId, Guid TriggerId), bool> _states = new();

    protected TriggerMonitorBase(Action<Guid, EventTrigger, bool> raise)
    {
        Raise = raise;
        Ui = Dispatcher.CurrentDispatcher;
    }

    public abstract void Reload(IReadOnlyList<WallpaperProfile> profiles);

    protected void ResetStates() => _states.Clear();

    protected void Report(Guid profileId, EventTrigger trigger, bool active)
    {
        var key = (profileId, trigger.Id);
        _states.TryGetValue(key, out var previous);
        if (previous == active)
        {
            return;
        }
        if (active)
        {
            _states[key] = true;
        }
        else
        {
            _states.Remove(key);
        }
        Raise(profileId, trigger, active);
    }

    public virtual void Dispose()
    {
    }
}
