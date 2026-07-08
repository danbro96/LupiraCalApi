namespace LupiraCalApi.Domain;

/// <summary>
/// Strongly-typed, kind-specific detail for a <see cref="CalendarItem"/> (table-per-type, realized as nested
/// records on the item snapshot). Populated members match <see cref="CalendarItem.Kind"/>: the travel family
/// (Flight/Train/Bus/Car) pairs its specialization with the common <see cref="TravelDetail"/>; every other kind
/// uses its single member (enforced by <c>KindDetailsMapper.Validate</c>). Location (hotel, clinic, venue) uses
/// the item's <c>PlaceId</c>; provider references reuse a <c>Contact</c> id.
/// </summary>
public sealed record ItemKindDetails(
    TravelDetail? Travel = null,
    FlightDetail? Flight = null,
    TrainDetail? Train = null,
    BusDetail? Bus = null,
    CarDetail? Car = null,
    LodgingDetail? Lodging = null,
    AppointmentDetail? Appointment = null,
    TicketedDetail? Ticketed = null,
    DeliveryDetail? Delivery = null,
    BillDetail? Bill = null,
    AvailabilityDetail? Availability = null);

/// <summary>A presence segment's status. The segment's span is the item itself (whole-day or timed <c>StartsAt</c>/<c>EndsAt</c>);
/// a recurring item carries the default week. The assistant resolves "status at instant T" from the covering segment(s).</summary>
public sealed record AvailabilityDetail(AvailabilityStatus Status);

/// <summary>Common travel fields; Flight/Train/Bus/Car specialize. <c>ToPlaceId</c> is the (required) destination.</summary>
public sealed record TravelDetail(
    Guid ToPlaceId, Guid? FromPlaceId, DateTimeOffset? DepartAt, DateTimeOffset? ArriveAt, string? Carrier, string? BookingReference);

public sealed record FlightDetail(
    string? FlightNumber, string? Terminal, string? Gate, DateTimeOffset? GateClosesAt, string? SeatAssignment, string? BaggageAllowance);

public sealed record TrainDetail(
    string? TrainNumber, string? Coach, string? Seat, string? DeparturePlatform, string? ArrivalPlatform);

public sealed record BusDetail(
    string? Operator, string? ServiceNumber, string? DepartureStop, string? ArrivalStop, string? SeatReservation);

public sealed record CarDetail(
    Guid? DriverContactId, string? Vehicle, string? LicensePlate, Guid? PickupPlaceId, Guid? DropoffPlaceId);

public sealed record LodgingDetail(
    string? ConfirmationNumber, DateTimeOffset? CheckInAt, DateTimeOffset? CheckOutAt, string? RoomType, string? Provider);

public sealed record AppointmentDetail(
    Guid? ProviderContactId, string? AppointmentType, string? ReferenceNumber, string? PreparationNotes);

public sealed record TicketedDetail(
    string? Performer, string? Seat, string? TicketReference, DateTimeOffset? DoorsOpenAt, string? Provider);

public sealed record DeliveryDetail(
    string? Carrier, string? TrackingNumber, string? TrackingUrl, string? OrderReference);

public sealed record BillDetail(
    decimal? Amount, string? Currency, string? Payee, string? InvoiceNumber, DateTimeOffset? PaidAt);
