using WallpaperProfiles.Infrastructure;
using WallpaperProfiles.Models;

namespace WallpaperProfiles.Resolution;

internal enum ResolutionSource
{
    Manual,
    Event,
    Schedule,
    Fallback,
    Retained,
}

internal sealed record ManualSwitch(Guid ProfileId, DateTime AtLocal, DateTimeOffset? UntilUtc = null);

internal sealed record ManualOverrideStatus(Guid ProfileId, DateTime? EndsAtLocal, bool IsTimed);

internal sealed record ActiveEventSnapshot(Guid ProfileId, EventTrigger Trigger, int Priority, DateTime SinceUtc);

internal sealed record ProfileDecision(Guid? ProfileId, ResolutionSource Source);

internal sealed class ProfileResolver
{
    private ManualSwitch? _manual;
    private readonly TimeProvider _timeProvider;

    public ProfileResolver(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    public void SetManual(Guid profileId, TimeSpan? duration = null)
    {
        if (duration is { } value && (value <= TimeSpan.Zero || value > TimeSpan.FromDays(1)))
            throw new ArgumentOutOfRangeException(nameof(duration), "An override must last between 0 and 24 hours.");
        _manual = new ManualSwitch(profileId, _timeProvider.GetLocalNow().DateTime,
            duration.HasValue ? _timeProvider.GetUtcNow().Add(duration.Value) : null);
    }

    public void ClearManual() => _manual = null;

    public ManualOverrideStatus? GetManualOverride(IReadOnlyList<WallpaperProfile> profiles)
    {
        if (_manual is not { } manual || !profiles.Any(p => p.Id == manual.ProfileId)) return null;
        var end = manual.UntilUtc.HasValue
            ? TimeZoneInfo.ConvertTime(manual.UntilUtc.Value, _timeProvider.LocalTimeZone).DateTime
            : NextBoundary(manual.AtLocal, profiles);
        return new ManualOverrideStatus(manual.ProfileId, end, manual.UntilUtc.HasValue);
    }

    public ProfileDecision Resolve(DateTime nowLocal, IReadOnlyList<WallpaperProfile> profiles,
        IReadOnlyCollection<ActiveEventSnapshot> activeEvents, Guid? fallbackProfileId = null)
    {
        if (_manual is { } manual)
        {
            var expired = manual.UntilUtc.HasValue
                ? _timeProvider.GetUtcNow() >= manual.UntilUtc.Value
                : NextBoundary(manual.AtLocal, profiles) is { } boundary && nowLocal >= boundary;
            if (!expired && profiles.Any(p => p.Id == manual.ProfileId))
            {
                return new ProfileDecision(manual.ProfileId, ResolutionSource.Manual);
            }
            _manual = null;
            Logger.Info("Manual override ended; handing control back to automatic rules.");
        }

        var active = activeEvents
            .Where(e => profiles.Any(p => p.Id == e.ProfileId))
            .OrderByDescending(e => e.Priority)
            .ThenByDescending(e => e.SinceUtc)
            .FirstOrDefault();
        if (active != null)
        {
            return new ProfileDecision(active.ProfileId, ResolutionSource.Event);
        }

        var scheduled = PickScheduled(nowLocal, profiles);
        if (scheduled.HasValue)
        {
            return new ProfileDecision(scheduled.Value.Profile.Id, ResolutionSource.Schedule);
        }

        if (fallbackProfileId is Guid fallback && profiles.Any(profile => profile.Id == fallback))
        {
            return new ProfileDecision(fallback, ResolutionSource.Fallback);
        }

        return new ProfileDecision(null, ResolutionSource.Schedule);
    }

    private static (WallpaperProfile Profile, ScheduleRule Rule)? PickScheduled(
        DateTime now, IReadOnlyList<WallpaperProfile> profiles)
    {
        (WallpaperProfile, ScheduleRule)? best = null;
        foreach (var profile in profiles)
        {
            foreach (var rule in profile.Schedule)
            {
                if (!rule.Matches(now.DayOfWeek, TimeOnly.FromDateTime(now)))
                {
                    continue;
                }
                if (best == null || rule.CreatedAtUtc > best.Value.Item2.CreatedAtUtc)
                {
                    best = (profile, rule);
                }
            }
        }
        return best;
    }

    public static WallpaperProfile? ScheduledProfileAt(DateTime nowLocal, IReadOnlyList<WallpaperProfile> profiles)
        => PickScheduled(nowLocal, profiles)?.Profile;

    public static DateTime? NextBoundary(DateTime afterLocal, IReadOnlyList<WallpaperProfile> profiles)
    {
        DateTime? best = null;
        for (var offset = -1; offset <= 8; offset++)
        {
            var day = DateOnly.FromDateTime(afterLocal).AddDays(offset);
            foreach (var profile in profiles)
            {
                foreach (var rule in profile.Schedule)
                {
                    if (rule.StartTime == rule.EndTime) continue;
                    if (!rule.DaysOfWeek.Contains(day.DayOfWeek))
                    {
                        continue;
                    }
                    var start = day.ToDateTime(rule.StartTime);
                    var endDay = rule.EndTime > rule.StartTime ? day : day.AddDays(1);
                    var end = endDay.ToDateTime(rule.EndTime);
                    foreach (var transition in new[] { start, end })
                    {
                        if (transition <= afterLocal)
                        {
                            continue;
                        }
                        if (best == null || transition < best)
                        {
                            best = transition;
                        }
                    }
                }
            }
        }
        return best;
    }
}
