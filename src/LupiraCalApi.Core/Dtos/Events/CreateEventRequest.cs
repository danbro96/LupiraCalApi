namespace LupiraCalApi.Dtos.Events;

public record CreateEventRequest(
    Guid CalendarId, string? Title, string? Description, string? Location, string? Status,
    bool IsAllDay, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? StartTimezone,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule, string[]? Tags);
