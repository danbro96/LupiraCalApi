using LupiraCalApi.Domain;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Event-replay behavior of the <see cref="CalendarItem"/> aggregate snapshot: membership/curation,
/// soft-delete + resurrection, status, metadata, and the attendee participation lifecycle.</summary>
public class CalendarItemTests
{
    static CalendarItemFields Fields() => new(
        "Lunch", "with team", ItemStatus.Confirmed, false,
        new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
        "UTC", null, null, null, null, null, null, ItemCategory.General, null, null, null, ["work"]);

    static CalendarItem Scheduled(Guid id)
    {
        var i = new CalendarItem();
        i.Apply(new ItemScheduled(id, "u@x", Fields(), null));
        return i;
    }

    [Fact]
    public void Scheduled_then_accepted_into_calendar_is_live_there()
    {
        var id = Guid.NewGuid();
        var cal = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(new AddedToCalendar(id, cal, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));

        Assert.True(i.IsAcceptedIn(cal));
        Assert.NotEmpty(i.ContentHash);   // derived from the canonical ICS in the snapshot, not carried on the event
        Assert.Equal(ItemStatus.Confirmed, i.Status);
        Assert.Equal(ItemCategory.General, i.Category);
    }

    [Fact]
    public void Proposed_membership_is_not_live_until_accepted()
    {
        var id = Guid.NewGuid();
        var cal = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(new AddedToCalendar(id, cal, CalendarEntryStatus.Proposed, DateTimeOffset.UtcNow));
        Assert.False(i.IsAcceptedIn(cal));

        i.Apply(new CalendarEntryStatusChanged(id, cal, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));
        Assert.True(i.IsAcceptedIn(cal));
    }

    [Fact]
    public void Removed_from_calendar_tombstones_the_membership()
    {
        var id = Guid.NewGuid();
        var cal = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(new AddedToCalendar(id, cal, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow));
        i.Apply(new RemovedFromCalendar(id, cal, DateTimeOffset.UtcNow));

        Assert.False(i.IsAcceptedIn(cal));
        Assert.Equal(CalendarEntryStatus.Removed, i.Calendars.Single(m => m.CalendarId == cal).Status);
    }

    [Fact]
    public void IcsPut_resurrects_a_soft_deleted_item()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        var scheduledHash = i.ContentHash;
        i.Apply(new ItemDeleted(id, DateTimeOffset.UtcNow));
        Assert.NotNull(i.DeletedAt);

        i.Apply(new ItemImported(id, "u@x", Fields()));
        Assert.Null(i.DeletedAt);
        Assert.Equal(scheduledHash, i.ContentHash);   // same canonical fields → same derived ETag
    }

    [Fact]
    public void Revised_updates_fields_and_content_hash()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        var before = i.ContentHash;
        i.Apply(new ItemRevised(id, Fields() with { Title = "Dinner" }, null));

        Assert.Equal("Dinner", i.Title);
        Assert.NotEqual(before, i.ContentHash);   // the title is in the canonical ICS, so the ETag moves
    }

    [Fact]
    public void Revised_reclassifies_the_category_and_sets_details()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);   // General, no details
        Assert.Equal(ItemCategory.General, i.Category);

        var trip = Fields() with { Category = ItemCategory.Trip };
        var details = new ItemDetails(Travel: new TravelLeg(TransportMode.Flight, Guid.NewGuid(), null, null, null, "SAS", "SK123", null, null, "14C", null));
        i.Apply(new ItemRevised(id, trip, details));

        Assert.Equal(ItemCategory.Trip, i.Category);
        Assert.Equal("SK123", i.Details!.Travel!.ServiceNumber);
    }

    [Fact]
    public void Revised_with_null_details_keeps_the_existing_details()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(new ItemRevised(id, Fields() with { Category = ItemCategory.Trip }, new ItemDetails(Travel: new TravelLeg(TransportMode.Flight, Guid.NewGuid(), null, null, null, null, "SK123", null, null, null, null))));

        i.Apply(new ItemRevised(id, Fields() with { Title = "Renamed" }, null));

        Assert.Equal("Renamed", i.Title);
        Assert.Equal("SK123", i.Details!.Travel!.ServiceNumber);   // details survive a field-only revision
    }

    [Fact]
    public void Cancelled_sets_status_without_deleting()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        var before = i.ContentHash;
        i.Apply(new ItemCancelled(id));

        Assert.Equal(ItemStatus.Cancelled, i.Status);
        Assert.Null(i.DeletedAt);
        Assert.NotEqual(before, i.ContentHash);   // STATUS:CANCELLED is in the canonical ICS
    }

    [Fact]
    public void Restored_clears_the_tombstone()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        var before = i.ContentHash;
        i.Apply(new ItemDeleted(id, DateTimeOffset.UtcNow));
        i.Apply(new ItemRestored(id));

        Assert.Null(i.DeletedAt);
        Assert.Equal(before, i.ContentHash);   // restore touches no canonical field
    }

    [Fact]
    public void Deleted_records_the_event_time_deterministically()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);
        var i = Scheduled(id);
        i.Apply(new ItemDeleted(id, at));

        Assert.Equal(at, i.DeletedAt);   // from the event, not the wall clock — replay is pure
    }

    [Fact]
    public void Metadata_attached_does_not_touch_the_content_hash()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        var before = i.ContentHash;
        i.Apply(new ItemMetadataAttached(id, """{"source":"import"}"""));

        Assert.Equal("""{"source":"import"}""", i.Metadata);
        Assert.Equal(before, i.ContentHash);   // metadata is server-side, not part of the ETag
    }

    static ItemPrompt SamplePrompt() => new(
        PromptIntent.EnrichRecord, null, "fill in the venue", OutputKind.RecordEdit, null, ModelTier.Small,
        FallbackMode.Retry, new PromptFire(PromptFireKind.OnStart, null, null), Enabled: true);

    static ItemAction SampleAction() => new(
        ActionKind.SendCheckIn, null, """{"message":"how did it go?"}""",
        new PromptFire(PromptFireKind.OnEnd, null, null), Enabled: true);

    [Fact]
    public void Setting_an_action_clears_a_prompt_so_the_payload_stays_single()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(new ItemPromptSet(id, SamplePrompt()));
        Assert.NotNull(i.Prompt);

        i.Apply(new ItemActionSet(id, SampleAction()));
        Assert.NotNull(i.Action);
        Assert.Null(i.Prompt);   // XOR enforced in Apply
    }

    [Fact]
    public void Setting_a_prompt_clears_an_action()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(new ItemActionSet(id, SampleAction()));
        i.Apply(new ItemPromptSet(id, SamplePrompt()));

        Assert.NotNull(i.Prompt);
        Assert.Null(i.Action);
    }

    [Fact]
    public void Clearing_a_prompt_removes_it_and_leaves_no_payload()
    {
        var id = Guid.NewGuid();
        var i = Scheduled(id);
        i.Apply(new ItemPromptSet(id, SamplePrompt()));
        i.Apply(new ItemPromptCleared(id));

        Assert.Null(i.Prompt);
        Assert.Null(i.Action);
    }

    [Fact]
    public void Participation_timestamps_are_composed_from_events()
    {
        var id = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var invitedAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var respondedAt = new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero);
        var attendedAt = new DateTimeOffset(2026, 7, 1, 9, 5, 0, TimeSpan.Zero);

        var i = Scheduled(id);
        i.Apply(new AttendeeInvited(id, pid, contact, ParticipationRole.RequiredParticipant, invitedAt));
        i.Apply(new InvitationResponded(id, pid, ParticipationStatus.Accepted, respondedAt));
        i.Apply(new AttendanceConfirmed(id, pid, attendedAt));

        var a = Assert.Single(i.Attendees);
        Assert.Equal(contact, a.ContactId);
        Assert.Equal(ParticipationStatus.Accepted, a.Status);
        Assert.Equal(invitedAt, a.InvitedAt);
        Assert.Equal(respondedAt, a.RespondedAt);
        Assert.Equal(attendedAt, a.AttendedAt);
        Assert.Null(a.LeftAt);
    }

    [Fact]
    public void Participant_left_records_the_departure_time()
    {
        var id = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var leftAt = new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero);

        var i = Scheduled(id);
        i.Apply(new AttendeeInvited(id, pid, Guid.NewGuid(), ParticipationRole.OptionalParticipant, DateTimeOffset.UtcNow));
        i.Apply(new ParticipantLeft(id, pid, leftAt));

        Assert.Equal(leftAt, Assert.Single(i.Attendees).LeftAt);
    }

    [Fact]
    public void Attendee_removed_drops_the_participation()
    {
        var id = Guid.NewGuid();
        var pid = Guid.NewGuid();

        var i = Scheduled(id);
        i.Apply(new AttendeeInvited(id, pid, Guid.NewGuid(), ParticipationRole.RequiredParticipant, DateTimeOffset.UtcNow));
        i.Apply(new AttendeeRemoved(id, pid));

        Assert.Empty(i.Attendees);
    }
}
