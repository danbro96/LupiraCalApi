namespace LupiraCalApi.Domain;

/// <summary>
/// Composable, category-independent detail for a <see cref="CalendarItem"/>: any of a reservation (<see cref="Booking"/>),
/// a movement leg (<see cref="Travel"/>, a <c>Trip</c> only), or an availability segment (<see cref="Presence"/>). Each is
/// an optional value object rather than a per-kind member, so one event can carry several at once (a booked flight sets both
/// <see cref="Booking"/> and <see cref="Travel"/>). Location (venue, hotel, clinic) uses the item's <c>PlaceId</c>
/// (a LupiraGeoApi place id) + <c>LocationLabel</c>; provider/driver references reuse a <c>Contact</c> id.
/// </summary>
public sealed record ItemDetails(
    BookingDetail? Booking = null,
    TravelLeg? Travel = null,
    PresenceDetail? Presence = null);

/// <summary>A reservation/confirmation attached to any category (a booked meal, a ticketed outing, a hotel, a flight).
/// <c>ProviderContactId</c> reuses a <c>Contact</c> (airline, hotel, restaurant, venue); <c>PartySize</c> covers a
/// table/seat reservation; <c>Amount</c>/<c>Currency</c> the paid cost.</summary>
public sealed record BookingDetail(
    Guid? ProviderContactId, string? ConfirmationNumber, string? Reference, string? Url,
    decimal? Amount, string? Currency, int? PartySize);

/// <summary>One leg of a <c>Trip</c>. Mode-agnostic: <c>DeparturePoint</c>/<c>ArrivalPoint</c> generalize gate/platform/stop,
/// <c>Carrier</c> the airline/operator, <c>ServiceNumber</c> the flight/train/service number. <c>ToPlaceId</c>/<c>FromPlaceId</c>
/// are LupiraGeoApi place ids with denormalized <c>ToLabel</c>/<c>FromLabel</c>; <c>DriverContactId</c> names the driver
/// for a <see cref="TransportMode.Car"/> leg.</summary>
public sealed record TravelLeg(
    TransportMode Mode, Guid? ToPlaceId, Guid? FromPlaceId, DateTimeOffset? DepartAt, DateTimeOffset? ArriveAt,
    string? Carrier, string? ServiceNumber, string? DeparturePoint, string? ArrivalPoint, string? Seat, Guid? DriverContactId,
    string? ToLabel = null, string? FromLabel = null);

/// <summary>A presence/availability segment's status; the span is the item itself (whole-day or timed). Replaces the old
/// Availability item kind — presence items live on the availability calendar and are exempt from completeness scoring.</summary>
public sealed record PresenceDetail(AvailabilityStatus Status);
