using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>
/// The structured, mutable fields of a <see cref="CalendarItem"/> — bundled so the REST/MCP authoring path and the
/// DAV PUT path converge on one shape. These fields are canonical (no raw blob is stored); <c>ContentHash</c> (the
/// ETag) is derived from the ICS regenerated from them, and they feed REST/MCP queries, search, and time-range.
/// A recurring item's per-occurrence deviations are <see cref="ExcludedOccurrences"/> (dropped starts),
/// <see cref="ExtraOccurrences"/> (one-off starts added to the series) and <see cref="OccurrenceOverrides"/>.
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
    DateTimeOffset[]? ExcludedOccurrences,
    DateTimeOffset[]? ExtraOccurrences,
    OccurrenceOverride[]? OccurrenceOverrides,
    ItemCategory? Category,
    Guid? PlaceId,
    string? LocationLabel,
    Guid? ParentItemId,
    string[]? Tags,
    // Trailing + defaulted so pre-existing serialized events (which lack these keys) and the DAV/other call sites
    // that don't set precision stay source- and wire-compatible; null ⇒ the date is exact/unqualified.
    DatePrecision? StartPrecision = null,
    DatePrecision? EndPrecision = null);
