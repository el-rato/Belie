namespace WallpaperProfiles.Models;

public class ScheduleRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public HashSet<DayOfWeek> DaysOfWeek { get; set; } = new();

    public TimeOnly StartTime { get; set; } = new(9, 0);

    public TimeOnly EndTime { get; set; } = new(17, 0);

    public bool Matches(DayOfWeek day, TimeOnly time)
    {
        if (!DaysOfWeek.Contains(day))
        {
            return false;
        }
        if (StartTime == EndTime)
        {
            return false;
        }
        return StartTime < EndTime
            ? time >= StartTime && time < EndTime
            : time >= StartTime || time < EndTime;
    }
}
