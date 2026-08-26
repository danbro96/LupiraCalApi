using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>One leg of a <c>Trip</c>. Mode-agnostic: <c>DeparturePoint</c>/<c>ArrivalPoint</c> generalize gate/platform/stop,
/// <c>Carrier</c> the airline/operator, <c>ServiceNumber</c> the flight/train/service number. <c>ToPlaceId</c>/<c>FromPlaceId</c>
/// are LupiraGeoApi place ids with denormalized <c>ToLabel</c>/<c>FromLabel</c>; <c>DriverContactId</c> names the driver
/// for a <see cref="TransportMode.Car"/> leg.</summary>
public sealed record TravelLeg(
    TransportMode Mode, Guid? ToPlaceId, Guid? FromPlaceId, DateTimeOffset? DepartAt, DateTimeOffset? ArriveAt,
    string? Carrier, string? ServiceNumber, string? DeparturePoint, string? ArrivalPoint, string? Seat, Guid? DriverContactId,
    string? ToLabel = null, string? FromLabel = null);
