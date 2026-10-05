using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Serialization;

/// <summary>Primitives parsed out of an iCalendar event, mapped into the item's structured fields (no blob is retained).
/// EXDATE/RDATE values and RECURRENCE-ID override VEVENTs arrive as the item's structured per-occurrence deviations.</summary>
public sealed record ParsedEvent(
    string? Uid, string? Title, string? Description, string? Location, ItemStatus? Status, ItemCategory? Category, bool IsAllDay,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? StartTimezone, string? EndTimezone,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule,
    DateTimeOffset[]? ExcludedOccurrences, DateTimeOffset[]? ExtraOccurrences, OccurrenceOverride[]? OccurrenceOverrides);
