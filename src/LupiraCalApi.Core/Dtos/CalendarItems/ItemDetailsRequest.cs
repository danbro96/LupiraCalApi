using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

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
