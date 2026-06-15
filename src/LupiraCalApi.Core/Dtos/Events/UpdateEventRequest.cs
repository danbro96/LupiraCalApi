namespace LupiraCalApi.Dtos.Events;

public record UpdateEventRequest(
    string? Title, string? Description, string? Location, string? Status,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? RecurrenceRule, string[]? Tags);
