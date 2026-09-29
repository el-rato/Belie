namespace WallpaperProfiles.Models;

public enum TriggerType
{
    OnBattery,
    Charging,
    BelowBatteryPercent,
    IdleForMinutes,
    ProcessLaunch,
}

public class EventTrigger
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TriggerType Type { get; set; }

    public string Condition { get; set; } = "";

    public int Priority { get; set; } = 5;

    public string Describe() => Type switch
    {
        TriggerType.OnBattery => "on battery",
        TriggerType.Charging => "plugged in",
        TriggerType.BelowBatteryPercent => $"battery ≤ {Condition}%",
        TriggerType.IdleForMinutes => $"idle ≥ {Condition} min",
        TriggerType.ProcessLaunch => $"'{Condition}' starts",
        _ => Type.ToString(),
    };
}
