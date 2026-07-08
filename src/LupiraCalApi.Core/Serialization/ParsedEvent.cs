namespace LupiraCalApi.Serialization;

/// <summary>Primitives parsed out of a client-PUT iCalendar event, mapped into the item's structured fields (no blob is
/// retained). <c>RecurrenceExceptions</c>/<c>RecurrenceOverrides</c> are the verbatim EXDATE/RDATE lines and RECURRENCE-ID
/// override VEVENTs — captured opaquely because the structured model has no per-instance-exception shape.</summary>
public sealed record ParsedEvent(
    string? Title, string? Description, string? Location, bool IsAllDay,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? StartTimezone, string? EndTimezone,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule,
    string? RecurrenceExceptions, string? RecurrenceOverrides);
