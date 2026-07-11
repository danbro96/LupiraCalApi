namespace LupiraCalApi.Domain;

/// <summary>
/// The structured, mutable fields of a <see cref="CalendarItem"/> — bundled so the REST/MCP authoring path and the
/// DAV PUT path converge on one shape. These fields are canonical (no raw blob is stored); <c>ContentHash</c> (the
/// ETag) is derived from the ICS regenerated from them, and they feed REST/MCP queries, search, and time-range.
/// <see cref="RecurrenceExceptions"/>/<see cref="RecurrenceOverrides"/> are the one exception: they carry the
/// EXDATE/RDATE lines and RECURRENCE-ID override VEVENTs verbatim, since the structured model has no shape for
/// per-instance exceptions — a DAV-only concern, re-emitted on GET so single-occurrence edits round-trip.
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
    string? RecurrenceExceptions,
    string? RecurrenceOverrides,
    ItemCategory? Category,
    Guid? PlaceId,
    string? LocationLabel,
    Guid? ParentItemId,
    string[]? Tags,
    // Trailing + defaulted so pre-existing serialized events (which lack these keys) and the DAV/other call sites
    // that don't set precision stay source- and wire-compatible; null ⇒ the date is exact/unqualified.
    DatePrecision? StartPrecision = null,
    DatePrecision? EndPrecision = null);
