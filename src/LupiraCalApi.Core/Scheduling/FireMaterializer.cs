using LupiraCalApi.Domain;

namespace LupiraCalApi.Scheduling;

public interface IFireMaterializer
{
    /// <summary>Expand an item's fired payload + recurrence into <see cref="ScheduledFireRow"/>s over [now, now+horizon].
    /// Empty when the item carries no payload or the payload is disabled.</summary>
    IReadOnlyList<ScheduledFireRow> Materialize(CalendarItem item, CalendarKind? calendarKind, DateTimeOffset now, TimeSpan horizon);
}

/// <summary>Pure expansion (no DB), so the row logic is unit-testable; the projection/sweep do the persistence.</summary>
public sealed class FireMaterializer(RecurrenceExpander expander) : IFireMaterializer
{
    public IReadOnlyList<ScheduledFireRow> Materialize(CalendarItem item, CalendarKind? calendarKind, DateTimeOffset now, TimeSpan horizon)
    {
        var (fire, enabled) = item.Prompt is { } p ? (p.Fire, p.Enabled)
            : item.Action is { } a ? (a.Fire, a.Enabled)
            : (null, false);
        if (fire is null || !enabled) return [];

        var windowEnd = now + horizon;
        var calendarId = item.Calendars.FirstOrDefault(m => m.Status == CalendarEntryStatus.Accepted)?.CalendarId ?? Guid.Empty;
        var promptRef = RefString(item.Prompt?.Target ?? item.Action?.Target);
        var expireAfter = ExpireAfter(fire, calendarKind);
        var duration = Duration(item);

        var rows = new List<ScheduledFireRow>();
        foreach (var occStart in OccurrenceStarts(item, now, windowEnd))
        {
            var at = FireAt(fire, occStart, duration, item.StartTimezone);
            if (at > windowEnd) continue;
            var dedupe = $"{item.Id:N}:{at.UtcDateTime:O}";
            rows.Add(new ScheduledFireRow(DeterministicGuid.From(dedupe), item.Id, calendarId, at, promptRef, expireAfter, dedupe));
        }
        return rows;
    }

    private IEnumerable<DateTimeOffset> OccurrenceStarts(CalendarItem item, DateTimeOffset now, DateTimeOffset windowEnd)
    {
        if (!string.IsNullOrWhiteSpace(item.RecurrenceRule))
            return expander.Expand(item, now, windowEnd);
        return BaseStart(item) is { } s && s <= windowEnd ? [s] : [];
    }

    private static DateTimeOffset? BaseStart(CalendarItem i)
    {
        if (i.StartsAt is { } s) return s;
        if (i.IsAllDay && i.StartDate is { } d) return new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);
        return null;
    }

    private static TimeSpan Duration(CalendarItem i)
    {
        if (i.StartsAt is { } s && i.EndsAt is { } e) return e - s;
        if (i.IsAllDay && i.StartDate is { } sd && i.EndDate is { } ed) return ed.ToDateTime(TimeOnly.MinValue) - sd.ToDateTime(TimeOnly.MinValue);
        return TimeSpan.Zero;
    }

    private static DateTimeOffset FireAt(PromptFire fire, DateTimeOffset occStart, TimeSpan duration, string? tz) => fire.Kind switch
    {
        PromptFireKind.OnStart => occStart,
        PromptFireKind.OnEnd => occStart + duration,
        PromptFireKind.Offset => occStart + TimeSpan.FromMinutes(fire.OffsetMinutes ?? 0),
        PromptFireKind.AllDayAt => AtLocalTime(occStart, fire.AllDayAt ?? new TimeOnly(9, 0), tz),
        _ => occStart,
    };

    private static DateTimeOffset AtLocalTime(DateTimeOffset occStart, TimeOnly time, string? tz)
    {
        var zone = ResolveZone(tz);
        var local = new DateTime(occStart.Year, occStart.Month, occStart.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo ResolveZone(string? tz)
    {
        if (string.IsNullOrWhiteSpace(tz)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(tz); } catch { return TimeZoneInfo.Utc; }
    }

    // expire_after keys off the fire timing (leave-by/reminder) then the calendar kind (doc Defaults; 24h fallback).
    private static TimeSpan ExpireAfter(PromptFire fire, CalendarKind? kind)
    {
        if (fire.Kind == PromptFireKind.Offset) return TimeSpan.FromMinutes(30);
        return kind switch
        {
            CalendarKind.LlmPrompts => TimeSpan.FromHours(6),
            CalendarKind.DevOps => TimeSpan.FromDays(3),
            _ => TimeSpan.FromHours(24),
        };
    }

    private static string? RefString(Ref? r) => r switch
    {
        null => null,
        { Kind: RefKind.External } => r.Url,
        { Id: { } id } => $"{r.Kind}:{id:N}",
        _ => null,
    };
}
