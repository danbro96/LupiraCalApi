using System.Text.Json.Serialization;

namespace LupiraCalApi.Domain;

/// <summary>iCalendar VEVENT <c>STATUS</c>.</summary>
public enum ItemStatus { Tentative, Confirmed, Cancelled }

/// <summary>Whether a calendar is part of the user's agenda (DAV/agenda-projected) or agent-managed system scaffolding (REST/DB-only, never DAV).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalendarClass>))]
public enum CalendarClass { Agenda, System }

/// <summary>Purpose of a calendar within the standard set. <c>Group</c> covers household/family/team.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalendarKind>))]
public enum CalendarKind { Personal, Group, Birthdays, Availability, Inbox, LlmPrompts, UserCheckIn, DevOps, FoodPlan, Generic }

/// <summary>What a calendar event is (its semantic identity). Reservation/travel/presence specifics are carried by
/// composable optionals on the item (see <see cref="ItemDetails"/>), not by this discriminator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemCategory>))]
public enum ItemCategory { General, Meeting, Appointment, Meal, Occasion, Outing, Trip, Stay, Activity, Focus, Chore }

/// <summary>Mode of a <see cref="TravelLeg"/>. Rail split: <c>Train</c> = mainline/commuter, <c>Metro</c> = rapid transit,
/// <c>Tram</c> = light/narrow-gauge rail; <c>Coach</c> = long-distance bus vs local <c>Bus</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransportMode>))]
public enum TransportMode { Flight, Train, Metro, Tram, Bus, Coach, Car, Ferry, Bike, Walk, Other }

/// <summary>A presence/availability segment's status. A day may hold several segments (whole-day or timed).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AvailabilityStatus>))]
public enum AvailabilityStatus { Office, Home, Vacation, Sick, Leave }

/// <summary>Curation state of a <see cref="CalendarItem"/> within a calendar. <c>Removed</c> is retained as a sync tombstone.</summary>
public enum CalendarEntryStatus { Proposed, Accepted, Removed }

/// <summary>iCalendar <c>ROLE</c>.</summary>
public enum ParticipationRole { Chair, RequiredParticipant, OptionalParticipant, NonParticipant }

/// <summary>iCalendar <c>PARTSTAT</c> (attendee RSVP).</summary>
public enum ParticipationStatus { NeedsAction, Accepted, Declined, Tentative, Delegated }
