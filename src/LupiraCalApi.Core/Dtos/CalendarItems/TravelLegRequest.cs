using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary><c>ToPlace</c>/<c>FromPlace</c> are free-text labels resolved to a LupiraGeoApi place id + label; <c>DriverContactId</c> is a <see cref="Contact"/> id.</summary>
public sealed class TravelLegRequest
{
    public TransportMode Mode { get; set; }

    public string? ToPlace { get; set; }

    public string? FromPlace { get; set; }

    /// <summary>Pre-resolved place ids (places-first imports), checked against geo like the item's PlaceId. When set, used
    /// instead of resolving <see cref="ToPlace"/>/<see cref="FromPlace"/> text; the text, if any, is kept as the label.</summary>
    public Guid? ToPlaceId { get; set; }

    public Guid? FromPlaceId { get; set; }

    public DateTimeOffset? DepartAt { get; set; }

    public DateTimeOffset? ArriveAt { get; set; }

    public string? Carrier { get; set; }

    public string? ServiceNumber { get; set; }

    public string? DeparturePoint { get; set; }

    public string? ArrivalPoint { get; set; }

    public string? Seat { get; set; }

    public Guid? DriverContactId { get; set; }
}
