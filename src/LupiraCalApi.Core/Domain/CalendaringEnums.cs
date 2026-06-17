namespace LupiraCalApi.Domain;

/// <summary>iCalendar VEVENT <c>STATUS</c>.</summary>
public enum ItemStatus { Tentative, Confirmed, Cancelled }

/// <summary>Specialized-item discriminator; selects the strongly-typed kind detail (see <see cref="ItemKindDetails"/>).</summary>
public enum ItemKind { Generic, Travel, Flight, Train, Bus, Car, Lodging, Appointment, Ticketed, Delivery, Bill }

/// <summary>Curation state of a <see cref="CalendarItem"/> within a calendar. <c>Removed</c> is retained as a sync tombstone.</summary>
public enum CalendarEntryStatus { Proposed, Accepted, Removed }

/// <summary>iCalendar <c>ROLE</c>.</summary>
public enum ParticipationRole { Chair, RequiredParticipant, OptionalParticipant, NonParticipant }

/// <summary>iCalendar <c>PARTSTAT</c> (attendee RSVP).</summary>
public enum ParticipationStatus { NeedsAction, Accepted, Declined, Tentative, Delegated }
