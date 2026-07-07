using LupiraCalApi.Domain;

namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>
/// Kind-specific detail input for create/update. Mirrors <see cref="ItemKindDetails"/>, but place references are
/// free-text labels (resolved to a <see cref="Place"/> server-side, like <c>Location</c>) and contact references are
/// ids. Populate the member(s) matching the item's <c>Kind</c> — a flight sets both <see cref="Travel"/> (booking ref,
/// from/to) and <see cref="Flight"/> (number, gate, seat). On update, a supplied member replaces that member wholesale;
/// omitted members are kept. The pure-data kinds reuse their domain records directly (no references to resolve).
/// </summary>
public sealed class ItemKindDetailsRequest
{
    public TravelDetailRequest? Travel { get; set; }
    public FlightDetail? Flight { get; set; }
    public TrainDetail? Train { get; set; }
    public BusDetail? Bus { get; set; }
    public CarDetailRequest? Car { get; set; }
    public LodgingDetail? Lodging { get; set; }
    public AppointmentDetailRequest? Appointment { get; set; }
    public TicketedDetail? Ticketed { get; set; }
    public DeliveryDetail? Delivery { get; set; }
    public BillDetail? Bill { get; set; }
}

/// <summary>Travel common fields; <c>ToPlace</c>/<c>FromPlace</c> are free-text labels resolved to a <see cref="Place"/>.</summary>
public sealed class TravelDetailRequest
{
    public string? ToPlace { get; set; }
    public string? FromPlace { get; set; }
    public DateTimeOffset? DepartAt { get; set; }
    public DateTimeOffset? ArriveAt { get; set; }
    public string? Carrier { get; set; }
    public string? BookingReference { get; set; }
}

/// <summary><c>DriverContactId</c> is a <see cref="Contact"/> id; pickup/dropoff are free-text place labels.</summary>
public sealed class CarDetailRequest
{
    public Guid? DriverContactId { get; set; }
    public string? Vehicle { get; set; }
    public string? LicensePlate { get; set; }
    public string? PickupPlace { get; set; }
    public string? DropoffPlace { get; set; }
}

/// <summary><c>ProviderContactId</c> is a <see cref="Contact"/> id.</summary>
public sealed class AppointmentDetailRequest
{
    public Guid? ProviderContactId { get; set; }
    public string? AppointmentType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? PreparationNotes { get; set; }
}
