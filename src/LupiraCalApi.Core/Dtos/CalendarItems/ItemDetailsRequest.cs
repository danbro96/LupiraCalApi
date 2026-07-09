using LupiraCalApi.Domain;

namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>
/// Composable detail input for create/update. <see cref="Booking"/> (reservation/confirmation) attaches to any category;
/// <see cref="Travel"/> applies to a <c>Trip</c> (its <c>ToPlace</c>/<c>FromPlace</c> are free-text labels resolved to a
/// a LupiraGeoApi place, like <c>Location</c>). A presence/availability segment is authored via the request's top-level
/// <c>Availability</c> field, not here. On update, a supplied member replaces that member wholesale; omitted members are kept.
/// </summary>
public sealed class ItemDetailsRequest
{
    public BookingDetail? Booking { get; set; }
    public TravelLegRequest? Travel { get; set; }
}

/// <summary><c>ToPlace</c>/<c>FromPlace</c> are free-text labels resolved to a LupiraGeoApi place id + label; <c>DriverContactId</c> is a <see cref="Contact"/> id.</summary>
public sealed class TravelLegRequest
{
    public TransportMode Mode { get; set; }
    public string? ToPlace { get; set; }
    public string? FromPlace { get; set; }
    public DateTimeOffset? DepartAt { get; set; }
    public DateTimeOffset? ArriveAt { get; set; }
    public string? Carrier { get; set; }
    public string? ServiceNumber { get; set; }
    public string? DeparturePoint { get; set; }
    public string? ArrivalPoint { get; set; }
    public string? Seat { get; set; }
    public Guid? DriverContactId { get; set; }
}
