using JasperFx.Events;
using LupiraCalApi.Serialization;

namespace LupiraCalApi.Domain;

/// <summary>One attendee's participation in an item — composed from the participation events (the timestamps are
/// the events' recorded times). "No-show" is derived (a past item where an expected attendee never confirmed).</summary>
public sealed class ItemAttendee
{
    public Guid ParticipationId { get; set; }
    public Guid ContactId { get; set; }
    public ParticipationRole Role { get; set; }
    public ParticipationStatus Status { get; set; } = ParticipationStatus.NeedsAction;
    public DateTimeOffset? InvitedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public DateTimeOffset? AttendedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}

/// <summary>An item's membership of a calendar (the <c>CalendarEntry</c> read model, embedded). <c>Removed</c> is kept as a sync tombstone.</summary>
public sealed class CalendarMembership
{
    public Guid CalendarId { get; set; }
    public CalendarEntryStatus Status { get; set; }
}

/// <summary>
/// The calendar item aggregate + inline snapshot. Calendar-independent: it lives in zero-or-many calendars
/// via <see cref="Calendars"/>. The structured fields are canonical; DAV regenerates the ICS on demand and <c>ContentHash</c>
/// (the ETag) is derived from that canonical form. Participation and composable details are embedded read models.
/// </summary>
public sealed class CalendarItem
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = "";

    public string? Title { get; set; }
    public string? Description { get; set; }
    public ItemStatus? Status { get; set; }
    public bool IsAllDay { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? StartTimezone { get; set; }
    public string? EndTimezone { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    /// <summary>Confidence of <see cref="StartDate"/>/<see cref="StartsAt"/> (and end). REST/MCP annotation; not in ICS/ETag.</summary>
    public DatePrecision? StartPrecision { get; set; }
    public DatePrecision? EndPrecision { get; set; }
    public string? RecurrenceRule { get; set; }
    public string? RecurrenceExceptions { get; set; }
    public string? RecurrenceOverrides { get; set; }
    public ItemCategory? Category { get; set; }
    public Guid? PlaceId { get; set; }
    /// <summary>Denormalized display label for <see cref="PlaceId"/> (the geo place's canonical name, or the raw
    /// free-text when geo didn't resolve it) — so ICS + read need no cross-service lookup.</summary>
    public string? LocationLabel { get; set; }
    public Guid? ParentItemId { get; set; }
    public string[]? Tags { get; set; }
    public ItemDetails? Details { get; set; }

    public string ContentHash { get; set; } = "";
    public string Metadata { get; set; } = "{}";

    // Event-bound payload (server-side only, never in ICS). Exactly one of these is set (XOR), enforced in Apply.
    public ItemPrompt? Prompt { get; set; }
    public ItemAction? Action { get; set; }

    public List<ItemAttendee> Attendees { get; set; } = new();
    public List<CalendarMembership> Calendars { get; set; } = new();
    public DateTimeOffset? DeletedAt { get; set; }

    // ---- projection stamps (server timeline; feed the sync cursor + DTO timestamps) ----

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Global event sequence of the last event applied — the per-item watermark the sync changes feed
    /// queries by (indexed). Bumped on every event, even one whose section guard rejects it.</summary>
    public long UpdatedSequence { get; set; }

    /// <summary>Stream version, populated by Marten's aggregate versioning.</summary>
    public int Version { get; set; }

    // ---- per-section LWW guards (see SectionLww): the (occurredAt, commandId) of each section's last winner ----

    public DateTimeOffset CoreTs { get; set; }
    public Guid CoreCmd { get; set; }
    public DateTimeOffset MetadataTs { get; set; }
    public Guid MetadataCmd { get; set; }
    public DateTimeOffset PayloadTs { get; set; }
    public Guid PayloadCmd { get; set; }
    public Dictionary<Guid, DateTimeOffset> FilingTs { get; set; } = new();
    public Dictionary<Guid, Guid> FilingCmd { get; set; } = new();

    /// <summary>Live in calendar <paramref name="calendarId"/> = an accepted membership and not soft-deleted.</summary>
    public bool IsAcceptedIn(Guid calendarId) =>
        DeletedAt is null && Calendars.Any(m => m.CalendarId == calendarId && m.Status == CalendarEntryStatus.Accepted);

    // ---- apply (create + mutate) ----

    public void Apply(IEvent<ItemScheduled> e)
    {
        Touch(e);
        Id = e.Data.ItemId;
        ExternalId = e.Data.ExternalId;
        SetFields(e.Data.Fields);
        Details = e.Data.Details;
        DeletedAt = null;
        // Creation seeds the core guard from the server stamp: a stale offline edit predating the create loses.
        (CoreTs, CoreCmd) = SectionLww.Stamp(e, null, null);
        RecomputeHash();
    }

    public void Apply(IEvent<ItemImported> e)
    {
        Touch(e);
        Id = e.Data.ItemId;
        ExternalId = e.Data.ExternalId;
        SetFields(e.Data.Parsed);
        DeletedAt = null;
        (CoreTs, CoreCmd) = SectionLww.Stamp(e, null, null);
        RecomputeHash();
    }

    public void Apply(IEvent<ItemRevised> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, CoreTs, CoreCmd)) return;
        SetFields(e.Data.Fields);
        if (e.Data.Details is not null) Details = e.Data.Details;
        (CoreTs, CoreCmd) = (ts, cmd);
        RecomputeHash();
    }

    public void Apply(IEvent<ItemCancelled> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, CoreTs, CoreCmd)) return;
        Status = ItemStatus.Cancelled;
        (CoreTs, CoreCmd) = (ts, cmd);
        RecomputeHash();
    }

    // Delete is absorbing (gates every section apply above/below); restore is the explicit inverse.
    public void Apply(IEvent<ItemDeleted> e) { Touch(e); DeletedAt = e.Data.At; }

    public void Apply(IEvent<ItemRestored> e) { Touch(e); DeletedAt = null; }

    /// <summary>The ETag is a pure function of the canonical ICS — recomputed here (in the snapshot projection) whenever
    /// a canonical field changes, never stored on the event, so a serializer fix heals every item on rebuild.</summary>
    private void RecomputeHash() => ContentHash = ICalSerializer.HashOf(this, LocationLabel);

    public void Apply(IEvent<ItemMetadataAttached> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, MetadataTs, MetadataCmd)) return;
        Metadata = e.Data.MetadataJson;
        (MetadataTs, MetadataCmd) = (ts, cmd);
    }

    // XOR: setting one payload clears the other so the snapshot is always single-payload. One shared guard —
    // prompt and action compete for the same slot, so they must also resolve against each other.
    public void Apply(IEvent<ItemPromptSet> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, PayloadTs, PayloadCmd)) return;
        Prompt = e.Data.Prompt;
        Action = null;
        (PayloadTs, PayloadCmd) = (ts, cmd);
    }

    public void Apply(IEvent<ItemPromptCleared> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, PayloadTs, PayloadCmd)) return;
        Prompt = null;
        (PayloadTs, PayloadCmd) = (ts, cmd);
    }

    public void Apply(IEvent<ItemActionSet> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, PayloadTs, PayloadCmd)) return;
        Action = e.Data.Action;
        Prompt = null;
        (PayloadTs, PayloadCmd) = (ts, cmd);
    }

    public void Apply(IEvent<ItemActionCleared> e)
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, e.Data.OccurredAt, e.Data.CommandId);
        if (DeletedAt is not null || !SectionLww.Wins(ts, cmd, PayloadTs, PayloadCmd)) return;
        Action = null;
        (PayloadTs, PayloadCmd) = (ts, cmd);
    }

    // Participation stays append-ordered (no section guard): edits are per-participation-id and rare enough that
    // cross-device conflicts resolve acceptably by append order; Idempotency-Key still dedups replays.
    public void Apply(IEvent<AttendeeInvited> e)
    {
        Touch(e);
        Attendees.Add(new ItemAttendee
        {
            ParticipationId = e.Data.ParticipationId,
            ContactId = e.Data.ContactId,
            Role = e.Data.Role,
            InvitedAt = e.Data.At,
        });
    }

    public void Apply(IEvent<InvitationResponded> e)
    {
        Touch(e);
        if (Find(e.Data.ParticipationId) is { } a) { a.Status = e.Data.Status; a.RespondedAt = e.Data.At; }
    }

    public void Apply(IEvent<AttendanceConfirmed> e)
    {
        Touch(e);
        if (Find(e.Data.ParticipationId) is { } a) a.AttendedAt = e.Data.At;
    }

    public void Apply(IEvent<ParticipantLeft> e)
    {
        Touch(e);
        if (Find(e.Data.ParticipationId) is { } a) a.LeftAt = e.Data.At;
    }

    public void Apply(IEvent<AttendeeRemoved> e)
    {
        Touch(e);
        Attendees.RemoveAll(a => a.ParticipationId == e.Data.ParticipationId);
    }

    // Filing guards are per calendar (like tags in the tasks LWW): unfiling from one calendar never races a
    // concurrent filing into another. At doubles as the stamp timestamp.
    public void Apply(IEvent<AddedToCalendar> e) => ApplyMembership(e, e.Data.CalendarId, e.Data.Status, e.Data.At, e.Data.CommandId);
    public void Apply(IEvent<CalendarEntryStatusChanged> e) => ApplyMembership(e, e.Data.CalendarId, e.Data.Status, e.Data.At, e.Data.CommandId);
    public void Apply(IEvent<RemovedFromCalendar> e) => ApplyMembership(e, e.Data.CalendarId, CalendarEntryStatus.Removed, e.Data.At, e.Data.CommandId);

    private void ApplyMembership<T>(IEvent<T> e, Guid calendarId, CalendarEntryStatus status, DateTimeOffset at, Guid? commandId) where T : class
    {
        Touch(e);
        var (ts, cmd) = SectionLww.Stamp(e, at, commandId);
        if (!SectionLww.Wins(ts, cmd, FilingTs.GetValueOrDefault(calendarId), FilingCmd.GetValueOrDefault(calendarId))) return;
        SetMembership(calendarId, status);
        FilingTs[calendarId] = ts;
        FilingCmd[calendarId] = cmd;
    }

    private void Touch<T>(IEvent<T> e) where T : class
    {
        if (CreatedAt == default) CreatedAt = e.Timestamp;
        if (e.Timestamp > UpdatedAt) UpdatedAt = e.Timestamp;
        UpdatedSequence = e.Sequence;
    }

    private ItemAttendee? Find(Guid participationId) => Attendees.FirstOrDefault(a => a.ParticipationId == participationId);

    private void SetMembership(Guid calendarId, CalendarEntryStatus status)
    {
        var m = Calendars.FirstOrDefault(x => x.CalendarId == calendarId);
        if (m is null) Calendars.Add(new CalendarMembership { CalendarId = calendarId, Status = status });
        else m.Status = status;
    }

    private void SetFields(CalendarItemFields f)
    {
        Title = f.Title;
        Description = f.Description;
        if (f.Status is { } s) Status = s;
        IsAllDay = f.IsAllDay;
        StartsAt = f.StartsAt;
        EndsAt = f.EndsAt;
        StartTimezone = f.StartTimezone;
        EndTimezone = f.EndTimezone;
        StartDate = f.StartDate;
        EndDate = f.EndDate;
        StartPrecision = f.StartPrecision;
        EndPrecision = f.EndPrecision;
        RecurrenceRule = f.RecurrenceRule;
        RecurrenceExceptions = f.RecurrenceExceptions;
        RecurrenceOverrides = f.RecurrenceOverrides;
        if (f.Category is { } c) Category = c;
        PlaceId = f.PlaceId;
        LocationLabel = f.LocationLabel;
        ParentItemId = f.ParentItemId;
        if (f.Tags is not null) Tags = f.Tags;
    }
}
