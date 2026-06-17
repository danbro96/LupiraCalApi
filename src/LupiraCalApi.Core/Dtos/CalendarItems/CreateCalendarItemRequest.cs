namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>Create an item via REST/MCP. <c>CalendarId</c> optional — when set, the item is accepted into that calendar;
/// when null, the item is created unfiled (e.g. an automated source) for later curation. <c>Location</c> is free text
/// resolved to a <see cref="LupiraCalApi.Domain.Place"/>. <c>Kind</c>/<c>Status</c> are the enum names.</summary>
public record CreateCalendarItemRequest(
    Guid? CalendarId, string? Title, string? Description, string? Location, string? Status,
    bool IsAllDay, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? StartTimezone,
    DateOnly? StartDate, DateOnly? EndDate, string? RecurrenceRule, string? Kind, string[]? Tags);
