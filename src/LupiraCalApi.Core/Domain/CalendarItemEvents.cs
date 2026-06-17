namespace LupiraCalApi.Domain;

// ---- lifecycle (the raw SourceIcalendar + ContentHash are the DAV source of truth; carried on the event) ----

/// <summary>Created via REST/MCP from structured fields (the service regenerated the ICS + hash first).</summary>
public record ItemScheduled(Guid ItemId, string IcalUid, CalendarItemFields Fields, ItemKindDetails? KindDetails, string SourceIcalendar, string ContentHash);

/// <summary>Created or replaced from a DAV/CardDAV-style raw ICS PUT (blob is authoritative; <c>Parsed</c> is a projection aid).</summary>
public record ItemIcsPut(Guid ItemId, string IcalUid, CalendarItemFields Parsed, string SourceIcalendar, string ContentHash);

/// <summary>Structured update of any subset of fields/kind-details (the service regenerated the ICS + hash).</summary>
public record ItemRevised(Guid ItemId, CalendarItemFields Fields, ItemKindDetails? KindDetails, string SourceIcalendar, string ContentHash);

/// <summary>STATUS → cancelled (distinct from deletion; the item still exists).</summary>
public record ItemCancelled(Guid ItemId, string SourceIcalendar, string ContentHash);

/// <summary>Soft delete (tombstone). The stream is never archived so sync stays diffable.</summary>
public record ItemDeleted(Guid ItemId);

/// <summary>Resurrects a soft-deleted item (e.g. DELETE-then-PUT of the same uid).</summary>
public record ItemRestored(Guid ItemId, string SourceIcalendar, string ContentHash);

/// <summary>Server-side free-form annotations (JSON). Does not change the ICS or its hash.</summary>
public record ItemMetadataAttached(Guid ItemId, string MetadataJson);

// ---- participation (first-class invited/attended history) ----

public record AttendeeInvited(Guid ItemId, Guid ParticipationId, Guid ContactId, ParticipationRole Role, DateTimeOffset At);
public record InvitationResponded(Guid ItemId, Guid ParticipationId, ParticipationStatus Status, DateTimeOffset At);
public record AttendanceConfirmed(Guid ItemId, Guid ParticipationId, DateTimeOffset At);
public record ParticipantLeft(Guid ItemId, Guid ParticipationId, DateTimeOffset At);
public record AttendeeRemoved(Guid ItemId, Guid ParticipationId);

// ---- membership / curation (CalendarItem ↔ Calendar is many-to-many) ----

public record AddedToCalendar(Guid ItemId, Guid CalendarId, CalendarEntryStatus Status, DateTimeOffset At);
public record CalendarEntryStatusChanged(Guid ItemId, Guid CalendarId, CalendarEntryStatus Status, DateTimeOffset At);
public record RemovedFromCalendar(Guid ItemId, Guid CalendarId, DateTimeOffset At);
