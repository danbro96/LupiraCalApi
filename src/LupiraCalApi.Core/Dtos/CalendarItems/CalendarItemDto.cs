using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.CalendarItems;

public record CalendarMembershipDto(Guid CalendarId, string Status);

public record CalendarItemDto(
    Guid Id, string IcalUid, string? Title, string? Description, string? Status, bool IsAllDay,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, DateOnly? StartDate, DateOnly? EndDate,
    string? RecurrenceRule, string? Kind, Guid? PlaceId, Guid? ParentItemId, string[]? Tags,
    JsonNode? Metadata, IReadOnlyList<CalendarMembershipDto> Calendars, string Etag);
