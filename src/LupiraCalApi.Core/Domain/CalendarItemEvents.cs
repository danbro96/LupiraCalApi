namespace LupiraCalApi.Domain;

// ---- lifecycle (structured fields are canonical; ICS is regenerated on demand. ContentHash = ETag, derived from the canonical form) ----

/// <summary>Created via REST/MCP from structured fields (the service derived the hash from the canonical ICS).</summary>
public record ItemScheduled(Guid ItemId, string ExternalId, CalendarItemFields Fields, ItemKindDetails? KindDetails, string ContentHash);

/// <summary>Created or replaced from a DAV PUT — parsed into structured fields (no blob retained); hash derived from the canonical form.</summary>
public record ItemImported(Guid ItemId, string ExternalId, CalendarItemFields Parsed, string ContentHash);

/// <summary>Structured update of any subset of fields/kind-details (hash re-derived from the canonical form).</summary>
public record ItemRevised(Guid ItemId, CalendarItemFields Fields, ItemKindDetails? KindDetails, string ContentHash);

/// <summary>STATUS → cancelled (distinct from deletion; the item still exists).</summary>
public record ItemCancelled(Guid ItemId, string ContentHash);

/// <summary>Soft delete (tombstone). The stream is never archived so sync stays diffable.</summary>
public record ItemDeleted(Guid ItemId);

/// <summary>Resurrects a soft-deleted item (e.g. DELETE-then-PUT of the same uid).</summary>
public record ItemRestored(Guid ItemId, string ContentHash);

/// <summary>Server-side free-form annotations (JSON). Does not change the ICS or its hash.</summary>
public record ItemMetadataAttached(Guid ItemId, string MetadataJson);

// ---- event-bound payload (server-side only — like metadata, never in ICS; an item carries one payload, XOR) ----

public record ItemPromptSet(Guid ItemId, ItemPrompt Prompt);
public record ItemPromptCleared(Guid ItemId);
public record ItemActionSet(Guid ItemId, ItemAction Action);
public record ItemActionCleared(Guid ItemId);

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
