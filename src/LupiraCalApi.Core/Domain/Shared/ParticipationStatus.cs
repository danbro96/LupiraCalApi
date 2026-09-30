namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>An attendee's RSVP.</summary>
public enum ParticipationStatus
{
    NeedsAction,
    Accepted,
    Declined,
    Tentative,
    Delegated,
}
