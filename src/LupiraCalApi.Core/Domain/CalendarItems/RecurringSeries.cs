using System.Diagnostics.CodeAnalysis;
using LupiraCalApi.Core.Domain.Shared;
using NodaTime;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>What places a series' occurrences: its start (an instant, or a date when all-day), the zone its wall clock
/// runs in, and its rule. Extra and deviating occurrences sit on top of it, on the item.</summary>
public sealed record RecurringSeries(bool IsAllDay, DateTimeOffset? StartsAt, DateOnly? StartDate, string? StartTimezone, string? RecurrenceRule)
{
    public static RecurringSeries Of(CalendarItem item) =>
        new(item.IsAllDay, item.StartsAt, item.StartDate, item.StartTimezone, item.RecurrenceRule);

    [MemberNotNullWhen(true, nameof(RecurrenceRule))]
    public bool Recurs => !string.IsNullOrWhiteSpace(RecurrenceRule);

    /// <summary>The first start and the zone the series recurs in — UTC for an all-day series (it recurs on dates) and
    /// for an absent or unknown zone. Null when the series has no start.</summary>
    public (Instant At, DateTimeZone Zone)? Anchor =>
        IsAllDay
            ? StartDate is { } d ? (Instant.FromUtc(d.Year, d.Month, d.Day, 0, 0), DateTimeZone.Utc) : null
            : StartsAt is { } s ? (Instant.FromDateTimeOffset(s), TimeZoneIds.Find(StartTimezone) ?? DateTimeZone.Utc) : null;
}
