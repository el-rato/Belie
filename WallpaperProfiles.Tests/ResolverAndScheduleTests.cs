using WallpaperProfiles.Models;
using WallpaperProfiles.Resolution;
using Xunit;

namespace WallpaperProfiles.Tests;

public class ScheduleRuleTests
{
    private static ScheduleRule Rule(DayOfWeek[] days, string start, string end) => new()
    {
        DaysOfWeek = new HashSet<DayOfWeek>(days),
        StartTime = TimeOnly.Parse(start),
        EndTime = TimeOnly.Parse(end),
    };

    [Fact]
    public void NormalWindow_MatchesInsideAndExcludesEnd()
    {
        var rule = Rule([DayOfWeek.Monday], "09:00", "17:00");
        var time = new TimeOnly(12, 0);

        Assert.True(rule.Matches(DayOfWeek.Monday, new TimeOnly(9, 0)));
        Assert.True(rule.Matches(DayOfWeek.Monday, time));
        // End time itself is exclusive, so adjacent rules don't double-match.
        Assert.False(rule.Matches(DayOfWeek.Monday, new TimeOnly(17, 0)));
    }

    [Fact]
    public void OvernightWindow_WrapsAroundMidnight()
    {
        var rule = Rule([DayOfWeek.Monday, DayOfWeek.Tuesday], "22:00", "06:00");

        // Monday evening onward and Tuesday early morning are both inside the window.
        Assert.True(rule.Matches(DayOfWeek.Monday, new TimeOnly(23, 30)));
        Assert.True(rule.Matches(DayOfWeek.Tuesday, new TimeOnly(2, 0)));
        Assert.False(rule.Matches(DayOfWeek.Tuesday, new TimeOnly(12, 0)));
        Assert.False(rule.Matches(DayOfWeek.Wednesday, new TimeOnly(2, 0)));
    }

    [Fact]
    public void EqualTimes_NeverMatch()
    {
        var rule = Rule([DayOfWeek.Friday], "09:00", "09:00");
        Assert.False(rule.Matches(DayOfWeek.Friday, new TimeOnly(9, 0)));
        Assert.False(rule.Matches(DayOfWeek.Friday, new TimeOnly(15, 0)));
    }

    [Fact]
    public void WrongDay_NeverMatches()
    {
        var rule = Rule([DayOfWeek.Monday], "09:00", "17:00");
        Assert.False(rule.Matches(DayOfWeek.Tuesday, new TimeOnly(12, 0)));
    }
}

public class ProfileResolverTests
{
    private static WallpaperProfile Profile(string name, params ScheduleRule[] rules) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Schedule = [.. rules],
    };

    private static ScheduleRule DailyRule(TimeOnly start, TimeOnly end, DateTime createdAtUtc)
    {
        var days = Enum.GetValues<DayOfWeek>();
        return new ScheduleRule
        {
            CreatedAtUtc = createdAtUtc,
            DaysOfWeek = new HashSet<DayOfWeek>(days),
            StartTime = start,
            EndTime = end,
        };
    }

    private static ActiveEventSnapshot Ev(Guid profileId, int priority, DateTime sinceUtc) =>
        new(profileId, new EventTrigger { Priority = priority, Type = TriggerType.OnBattery }, priority, sinceUtc);

    [Fact]
    public void ManualSwitch_WinsOverScheduleAndEvents()
    {
        var resolver = new ProfileResolver();
        var scheduled = Profile("Scheduled", DailyRule(new TimeOnly(0, 0), new TimeOnly(23, 59), DateTime.UtcNow));
        var manual = Profile("Manual");
        resolver.SetManual(manual.Id);

        var decision = resolver.Resolve(DateTime.Now, [scheduled, manual], [Ev(scheduled.Id, 99, DateTime.UtcNow)]);

        Assert.Equal(manual.Id, decision.ProfileId);
        Assert.Equal(ResolutionSource.Manual, decision.Source);
    }

    [Fact]
    public void ManualSwitch_WithoutRules_NeverExpires()
    {
        var resolver = new ProfileResolver();
        var manual = Profile("Manual");
        resolver.SetManual(manual.Id);

        // Far future: with no schedule rules there is no boundary, so manual holds.
        var decision = resolver.Resolve(DateTime.Now.AddDays(30), [manual], []);

        Assert.Equal(manual.Id, decision.ProfileId);
    }

    [Fact]
    public void ManualSwitch_ExpiresAtNextScheduledBoundary()
    {
        var resolver = new ProfileResolver();
        var tomorrow = DateTime.Now.Date.AddDays(1);
        var scheduled = Profile("Scheduled", new ScheduleRule
        {
            CreatedAtUtc = DateTime.UtcNow,
            DaysOfWeek = new HashSet<DayOfWeek> { tomorrow.DayOfWeek },
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
        });
        var manual = Profile("Manual");
        resolver.SetManual(manual.Id);

        // Before tomorrow's rule starts: manual still wins.
        var before = resolver.Resolve(tomorrow.AddDays(-1).AddHours(10), [scheduled, manual], []);
        Assert.Equal(ResolutionSource.Manual, before.Source);

        // Inside tomorrow's rule window: manual expired, schedule takes over.
        var inside = resolver.Resolve(tomorrow.AddHours(12), [scheduled, manual], []);
        Assert.Equal(scheduled.Id, inside.ProfileId);
        Assert.Equal(ResolutionSource.Schedule, inside.Source);
    }

    [Fact]
    public void HighestPriorityEvent_Wins_And_TiesBreakByMostRecent()
    {
        var resolver = new ProfileResolver();
        var low = Profile("Low");
        var high = Profile("High");
        var other = Profile("Other");

        var decision = resolver.Resolve(DateTime.Now, [low, high, other],
            [Ev(low.Id, 10, DateTime.UtcNow), Ev(high.Id, 50, DateTime.UtcNow)]);
        Assert.Equal(high.Id, decision.ProfileId);

        var tied = resolver.Resolve(DateTime.Now, [low, high, other],
            [Ev(low.Id, 50, DateTime.UtcNow.AddMinutes(-5)), Ev(high.Id, 50, DateTime.UtcNow)]);
        Assert.Equal(high.Id, tied.ProfileId);
    }

    [Fact]
    public void OverlappingScheduleRules_MostRecentlyCreated_Wins()
    {
        var resolver = new ProfileResolver();
        var older = Profile("Older", DailyRule(new TimeOnly(0, 0), new TimeOnly(23, 59), DateTime.UtcNow.AddDays(-2)));
        var newer = Profile("Newer", DailyRule(new TimeOnly(0, 0), new TimeOnly(23, 59), DateTime.UtcNow.AddDays(-1)));

        var decision = resolver.Resolve(DateTime.Now, [older, newer], []);

        Assert.Equal(newer.Id, decision.ProfileId);
    }

    [Fact]
    public void NothingMatches_ReturnsNullDecision()
    {
        var resolver = new ProfileResolver();
        var empty = Profile("Empty");

        var decision = resolver.Resolve(DateTime.Now, [empty], []);

        Assert.Null(decision.ProfileId);
    }
}
