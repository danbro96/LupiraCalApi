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

    /// <summary>Live in calendar <paramref name="calendarId"/> = an accepted membership and not soft-deleted.</summary>
    public bool IsAcceptedIn(Guid calendarId) =>
        DeletedAt is null && Calendars.Any(m => m.CalendarId == calendarId && m.Status == CalendarEntryStatus.Accepted);

    // ---- apply (create + mutate) ----

    public void Apply(ItemScheduled e)
    {
        Id = e.ItemId;
        ExternalId = e.ExternalId;
        SetFields(e.Fields);
        Details = e.Details;
        DeletedAt = null;
        RecomputeHash();
    }

    public void Apply(ItemImported e)
    {
        Id = e.ItemId;
        ExternalId = e.ExternalId;
        SetFields(e.Parsed);
        DeletedAt = null;
        RecomputeHash();
    }

    public void Apply(ItemRevised e)
    {
        SetFields(e.Fields);
        if (e.Details is not null) Details = e.Details;
        RecomputeHash();
    }

    public void Apply(ItemCancelled _)
    {
        Status = ItemStatus.Cancelled;
        RecomputeHash();
    }

    public void Apply(ItemDeleted e) => DeletedAt = e.At;

    public void Apply(ItemRestored _) => DeletedAt = null;

    /// <summary>The ETag is a pure function of the canonical ICS — recomputed here (in the snapshot projection) whenever
    /// a canonical field changes, never stored on the event, so a serializer fix heals every item on rebuild.</summary>
    private void RecomputeHash() => ContentHash = ICalSerializer.HashOf(this, LocationLabel);

    public void Apply(ItemMetadataAttached e) => Metadata = e.MetadataJson;

    // XOR: setting one payload clears the other so the snapshot is always single-payload.
    public void Apply(ItemPromptSet e) { Prompt = e.Prompt; Action = null; }
    public void Apply(ItemPromptCleared _) => Prompt = null;
    public void Apply(ItemActionSet e) { Action = e.Action; Prompt = null; }
    public void Apply(ItemActionCleared _) => Action = null;

    public void Apply(AttendeeInvited e) => Attendees.Add(new ItemAttendee
    {
        ParticipationId = e.ParticipationId,
        ContactId = e.ContactId,
        Role = e.Role,
        InvitedAt = e.At,
    });

    public void Apply(InvitationResponded e)
    {
        if (Find(e.ParticipationId) is { } a) { a.Status = e.Status; a.RespondedAt = e.At; }
    }

    public void Apply(AttendanceConfirmed e)
    {
        if (Find(e.ParticipationId) is { } a) a.AttendedAt = e.At;
    }

    public void Apply(ParticipantLeft e)
    {
        if (Find(e.ParticipationId) is { } a) a.LeftAt = e.At;
    }

    public void Apply(AttendeeRemoved e) => Attendees.RemoveAll(a => a.ParticipationId == e.ParticipationId);

    public void Apply(AddedToCalendar e) => SetMembership(e.CalendarId, e.Status);
    public void Apply(CalendarEntryStatusChanged e) => SetMembership(e.CalendarId, e.Status);
    public void Apply(RemovedFromCalendar e) => SetMembership(e.CalendarId, CalendarEntryStatus.Removed);

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
