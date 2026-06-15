using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.Events;

public record EventDto(
    Guid Id, Guid CalendarId, string IcalUid, string? Title, string? Description, string? Location,
    string? Status, bool IsAllDay, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule, string[]? Tags, JsonNode? Metadata, string Etag);
