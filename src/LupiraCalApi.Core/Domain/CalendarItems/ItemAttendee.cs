using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems;

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
