namespace LupiraCalApi.Core.Domain.CalendarItems;

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
