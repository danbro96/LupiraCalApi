using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Serialization;

/// <summary>Primitives parsed out of a client-PUT iCalendar event, mapped into the item's structured fields (no blob is
/// retained). EXDATE/RDATE values and RECURRENCE-ID override VEVENTs arrive as the item's structured per-occurrence
/// deviations.</summary>
public sealed record ParsedEvent(
    string? Title, string? Description, string? Location, bool IsAllDay,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? StartTimezone, string? EndTimezone,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule,
    DateTimeOffset[]? ExcludedOccurrences, DateTimeOffset[]? ExtraOccurrences, OccurrenceOverride[]? OccurrenceOverrides);
