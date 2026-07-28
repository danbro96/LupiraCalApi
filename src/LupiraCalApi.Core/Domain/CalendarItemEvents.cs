namespace LupiraCalApi.Domain;

// ---- lifecycle (structured fields are the ONLY canonical state; the ContentHash/ETag is NOT stored on events —
//      it is a pure derivation of the canonical ICS, recomputed in the snapshot so a formatter fix heals on rebuild) ----

/// <summary>Created via REST/MCP from structured fields.</summary>
public record ItemScheduled(Guid ItemId, string ExternalId, CalendarItemFields Fields, ItemDetails? Details);

/// <summary>Created or replaced from a DAV PUT — parsed into structured fields (no blob retained).</summary>
public record ItemImported(Guid ItemId, string ExternalId, CalendarItemFields Parsed);

// Mutating events carry an optional client stamp (OccurredAt = the client's wall clock, CommandId = its minted
// UUIDv7) — trailing + defaulted so pre-existing serialized events and non-stamping writers (web, DAV) stay wire-
// compatible. The stamp feeds SectionLww: a stale offline replay loses to a newer write instead of clobbering it.
// Unstamped events fall back to the event's server timestamp + sequence, preserving append order.

/// <summary>Structured update of any subset of fields/details.</summary>
public record ItemRevised(Guid ItemId, CalendarItemFields Fields, ItemDetails? Details,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

/// <summary>STATUS → cancelled (distinct from deletion; the item still exists).</summary>
public record ItemCancelled(Guid ItemId, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

/// <summary>Soft delete (tombstone). The stream is never archived so sync stays diffable. <c>At</c> is the recorded
/// deletion time — carried on the event (not read from the clock in Apply) so replay is deterministic.</summary>
public record ItemDeleted(Guid ItemId, DateTimeOffset At);

/// <summary>Resurrects a soft-deleted item (e.g. DELETE-then-PUT of the same uid).</summary>
public record ItemRestored(Guid ItemId);

/// <summary>Server-side free-form annotations (JSON). Does not change the ICS or its hash.</summary>
public record ItemMetadataAttached(Guid ItemId, string MetadataJson,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

// ---- event-bound payload (server-side only — like metadata, never in ICS; an item carries one payload, XOR) ----

public record ItemPromptSet(Guid ItemId, ItemPrompt Prompt, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
public record ItemPromptCleared(Guid ItemId, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
public record ItemActionSet(Guid ItemId, ItemAction Action, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
public record ItemActionCleared(Guid ItemId, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);

// ---- participation (first-class invited/attended history) ----

public record AttendeeInvited(Guid ItemId, Guid ParticipationId, Guid ContactId, ParticipationRole Role, DateTimeOffset At);
public record InvitationResponded(Guid ItemId, Guid ParticipationId, ParticipationStatus Status, DateTimeOffset At);
public record AttendanceConfirmed(Guid ItemId, Guid ParticipationId, DateTimeOffset At);
public record ParticipantLeft(Guid ItemId, Guid ParticipationId, DateTimeOffset At);
public record AttendeeRemoved(Guid ItemId, Guid ParticipationId);

// ---- membership / curation (CalendarItem ↔ Calendar is many-to-many) ----
// At doubles as the filing section's LWW timestamp (per calendar), so a client stamp goes into At.

public record AddedToCalendar(Guid ItemId, Guid CalendarId, CalendarEntryStatus Status, DateTimeOffset At, Guid? CommandId = null);
public record CalendarEntryStatusChanged(Guid ItemId, Guid CalendarId, CalendarEntryStatus Status, DateTimeOffset At, Guid? CommandId = null);
public record RemovedFromCalendar(Guid ItemId, Guid CalendarId, DateTimeOffset At, Guid? CommandId = null);
