namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>A newly added attendee: the contact and its assigned participation id.</summary>
public sealed record ParticipationRef(Guid ContactId, Guid ParticipationId);
