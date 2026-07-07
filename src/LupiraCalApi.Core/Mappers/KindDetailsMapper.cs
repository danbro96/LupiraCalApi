using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;

namespace LupiraCalApi.Mappers;

/// <summary>Builds the domain <see cref="ItemKindDetails"/> carrier from the request payload, resolving free-text place
/// labels to <see cref="Place"/> ids (via <see cref="PlaceService"/>), and merges a partial update onto existing details.</summary>
internal static class KindDetailsMapper
{
    /// <summary>Resolve the request into a details carrier for <paramref name="kind"/>. <c>Availability</c> keeps its
    /// dedicated top-level field. Returns null when there is nothing to set (so the caller preserves existing details).</summary>
    public static async Task<ItemKindDetails?> BuildAsync(
        ItemKind? kind, ItemKindDetailsRequest? d, AvailabilityStatus? availability, PlaceService places, CancellationToken ct)
    {
        if (kind == ItemKind.Availability)
            return availability is { } s ? new ItemKindDetails(Availability: new AvailabilityDetail(s)) : null;
        if (d is null) return null;

        TravelDetail? travel = null;
        if (d.Travel is { } t)
            travel = new TravelDetail(
                await places.ResolveLabelAsync(t.ToPlace, ct) ?? Guid.Empty,
                await places.ResolveLabelAsync(t.FromPlace, ct), t.DepartAt, t.ArriveAt, t.Carrier, t.BookingReference);

        CarDetail? car = null;
        if (d.Car is { } c)
            car = new CarDetail(c.DriverContactId, c.Vehicle, c.LicensePlate,
                await places.ResolveLabelAsync(c.PickupPlace, ct), await places.ResolveLabelAsync(c.DropoffPlace, ct));

        AppointmentDetail? appointment = d.Appointment is { } a
            ? new AppointmentDetail(a.ProviderContactId, a.AppointmentType, a.ReferenceNumber, a.PreparationNotes)
            : null;

        return new ItemKindDetails(
            Travel: travel, Flight: d.Flight, Train: d.Train, Bus: d.Bus, Car: car, Lodging: d.Lodging,
            Appointment: appointment, Ticketed: d.Ticketed, Delivery: d.Delivery, Bill: d.Bill);
    }

    /// <summary>Member-level merge: each populated member of <paramref name="incoming"/> replaces the same member on
    /// <paramref name="existing"/>; omitted members are kept. (A member is replaced wholesale, not field-merged.)</summary>
    public static ItemKindDetails Merge(ItemKindDetails? existing, ItemKindDetails incoming) =>
        existing is null ? incoming : new ItemKindDetails(
            Travel: incoming.Travel ?? existing.Travel,
            Flight: incoming.Flight ?? existing.Flight,
            Train: incoming.Train ?? existing.Train,
            Bus: incoming.Bus ?? existing.Bus,
            Car: incoming.Car ?? existing.Car,
            Lodging: incoming.Lodging ?? existing.Lodging,
            Appointment: incoming.Appointment ?? existing.Appointment,
            Ticketed: incoming.Ticketed ?? existing.Ticketed,
            Delivery: incoming.Delivery ?? existing.Delivery,
            Bill: incoming.Bill ?? existing.Bill,
            Availability: incoming.Availability ?? existing.Availability);
}
