namespace LupiraCalApi.Domain;

/// <summary>
/// The structured, mutable fields of a <see cref="CalendarItem"/> — bundled so the REST/MCP authoring path and the
/// DAV raw-ICS path converge on one shape. The raw <c>SourceIcalendar</c> blob + <c>ContentHash</c> ride alongside
/// on the event (they are the DAV source of truth); these fields feed REST/MCP queries, search, and time-range.
/// </summary>
public sealed record CalendarItemFields(
    string? Title,
    string? Description,
    ItemStatus? Status,
    bool IsAllDay,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? StartTimezone,
    string? EndTimezone,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? RecurrenceRule,
    ItemKind? Kind,
    Guid? PlaceId,
    Guid? ParentItemId,
    string[]? Tags);
