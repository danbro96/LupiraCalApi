namespace LupiraCalApi.Serialization;

/// <summary>Projection parsed out of a client-PUT VEVENT (the raw blob remains the source of truth).</summary>
public sealed record ParsedEvent(
    string? Title, string? Description, string? Location, bool IsAllDay,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? StartTimezone, string? EndTimezone,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule);
