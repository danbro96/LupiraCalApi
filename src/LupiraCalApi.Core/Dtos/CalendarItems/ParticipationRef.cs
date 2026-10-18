namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>A newly added attendee: the contact and its assigned participation id.</summary>
public sealed class ParticipationRef
{
    public required Guid ContactId { get; set; }

    public required Guid ParticipationId { get; set; }
}
