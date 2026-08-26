namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>iCalendar <c>PARTSTAT</c> (attendee RSVP).</summary>
public enum ParticipationStatus { NeedsAction, Accepted, Declined, Tentative, Delegated }
