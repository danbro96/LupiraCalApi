namespace LupiraCalApi.Dtos.CalendarItems;

public record UpdateCalendarItemRequest(
    string? Title, string? Description, string? Location, string? Status,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? RecurrenceRule, string[]? Tags);
