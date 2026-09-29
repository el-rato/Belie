using System.ComponentModel;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.UI;

public static class EnumLists
{
    public static readonly FitMode[] FitModes = (FitMode[])Enum.GetValues(typeof(FitMode));

    public static readonly IReadOnlyList<string> Times = Enumerable.Range(0, 96)
        .Select(i => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(i * 15)).ToString("HH:mm"))
        .ToList();

    public static readonly KeyValuePair<TriggerType, string>[] TriggerTypes =
    {
        new(TriggerType.OnBattery, "Power: running on battery"),
        new(TriggerType.Charging, "Power: plugged in / charging"),
        new(TriggerType.BelowBatteryPercent, "Power: battery below N%"),
        new(TriggerType.IdleForMinutes, "Input: user idle >= N minutes"),
        new(TriggerType.ProcessLaunch, "A program starts"),
    };
}

public sealed class ScheduleRuleVm : INotifyPropertyChanged
{
    public ScheduleRule Model { get; }

    public ScheduleRuleVm(ScheduleRule model)
    {
        Model = model;
        Days = new bool[7];
        foreach (var day in model.DaysOfWeek)
        {
            Days[(int)day] = true;
        }
        _startTimeText = model.StartTime.ToString("HH:mm");
        _endTimeText = model.EndTime.ToString("HH:mm");
    }

    public bool[] Days { get; private set; }

    private string _startTimeText;
    public string StartTimeText
    {
        get => _startTimeText;
        set { _startTimeText = value; OnPropertyChanged(nameof(StartTimeText)); }
    }

    private string _endTimeText;
    public string EndTimeText
    {
        get => _endTimeText;
        set { _endTimeText = value; OnPropertyChanged(nameof(EndTimeText)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TriggerVm : INotifyPropertyChanged
{
    public EventTrigger Model { get; }

    public TriggerVm(EventTrigger model)
    {
        Model = model;
        _type = model.Type;
        _condition = model.Condition;
        _priorityText = model.Priority.ToString();
    }

    private TriggerType _type;
    public TriggerType Type
    {
        get => _type;
        set
        {
            _type = value;
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(Hint));
        }
    }

    private string _condition;
    public string Condition
    {
        get => _condition;
        set { _condition = value; OnPropertyChanged(nameof(Condition)); }
    }

    private string _priorityText;
    public string PriorityText
    {
        get => _priorityText;
        set { _priorityText = value; OnPropertyChanged(nameof(PriorityText)); }
    }

    public string Hint => Type switch
    {
        TriggerType.BelowBatteryPercent => "battery percentage, e.g. 20",
        TriggerType.IdleForMinutes => "minutes, e.g. 15",
        TriggerType.ProcessLaunch => "program name, e.g. notepad.exe",
        _ => "no extra input needed",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
