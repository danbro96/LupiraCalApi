using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;

namespace LupiraCalApi.Mappers;

/// <summary>Builds the domain <see cref="ItemKindDetails"/> carrier from the request payload, resolving free-text place
/// labels to <see cref="Place"/> ids (via <see cref="PlaceService"/>), and merges a partial update onto existing details.</summary>
internal static class KindDetailsMapper
{
    // The travel family (Flight/Train/Bus/Car) pairs its specialization with the common Travel record;
    // every other kind uses its single member. Generic and Availability carry no kind-details members.
    private static readonly IReadOnlyDictionary<ItemKind, string[]> Allowed = new Dictionary<ItemKind, string[]>
    {
        [ItemKind.Travel] = ["Travel"],
        [ItemKind.Flight] = ["Travel", "Flight"],
        [ItemKind.Train] = ["Travel", "Train"],
        [ItemKind.Bus] = ["Travel", "Bus"],
        [ItemKind.Car] = ["Travel", "Car"],
        [ItemKind.Lodging] = ["Lodging"],
        [ItemKind.Appointment] = ["Appointment"],
        [ItemKind.Ticketed] = ["Ticketed"],
        [ItemKind.Delivery] = ["Delivery"],
        [ItemKind.Bill] = ["Bill"],
    };

    /// <summary>Request-level consistency check; null = valid. Only populated members count (an all-null
    /// <c>KindDetails</c> is a no-op for every kind). Deliberately does NOT judge resulting item state — a Flight
    /// with only its <c>Flight</c> member is legal (progressive enrichment; Completeness surfaces the gaps).</summary>
    public static string? Validate(ItemKind? kind, ItemKindDetailsRequest? details, AvailabilityStatus? availability)
    {
        if (availability is not null && kind != ItemKind.Availability)
            return $"Availability applies only to kind 'Availability', not '{KindName(kind)}'.";

        string[] populated = details is null ? [] : PopulatedMembers(details).ToArray();
        if (populated.Length == 0) return null;

        if (kind == ItemKind.Availability)
            return "Kind 'Availability' uses the top-level Availability field, not KindDetails.";

        string[] allowed = kind is { } k && Allowed.TryGetValue(k, out var a) ? a : [];
        if (populated.FirstOrDefault(m => !allowed.Contains(m)) is { } offending)
            return allowed.Length == 0
                ? $"KindDetails.{offending} does not apply to kind '{KindName(kind)}' (no kind-details members apply)."
                : $"KindDetails.{offending} does not apply to kind '{kind}' (allowed: {string.Join(", ", allowed)}).";

        if (details!.Travel is { } t && string.IsNullOrWhiteSpace(t.ToPlace))
            return "Travel.ToPlace is required (members replace wholesale — resend the full Travel member including ToPlace).";

        return null;
    }

    private static string KindName(ItemKind? kind) => kind?.ToString() ?? "none";

    private static IEnumerable<string> PopulatedMembers(ItemKindDetailsRequest d)
    {
        if (d.Travel is not null) yield return nameof(d.Travel);
        if (d.Flight is not null) yield return nameof(d.Flight);
        if (d.Train is not null) yield return nameof(d.Train);
        if (d.Bus is not null) yield return nameof(d.Bus);
        if (d.Car is not null) yield return nameof(d.Car);
        if (d.Lodging is not null) yield return nameof(d.Lodging);
        if (d.Appointment is not null) yield return nameof(d.Appointment);
        if (d.Ticketed is not null) yield return nameof(d.Ticketed);
        if (d.Delivery is not null) yield return nameof(d.Delivery);
        if (d.Bill is not null) yield return nameof(d.Bill);
    }

    /// <summary>Resolve the request into a details carrier for <paramref name="kind"/>. <c>Availability</c> keeps its
    /// dedicated top-level field. Returns null when there is nothing to set (so the caller preserves existing details).
    /// Callers must <see cref="Validate"/> first.</summary>
    public static async Task<ItemKindDetails?> BuildAsync(
        ItemKind? kind, ItemKindDetailsRequest? d, AvailabilityStatus? availability, PlaceService places, CancellationToken ct)
    {
        if (kind == ItemKind.Availability)
            return availability is { } s ? new ItemKindDetails(Availability: new AvailabilityDetail(s)) : null;
        if (d is null) return null;

        TravelDetail? travel = null;
        if (d.Travel is { } t)
            travel = new TravelDetail(
                await places.ResolveLabelAsync(t.ToPlace, ct)
                    ?? throw new InvalidOperationException("Travel.ToPlace must be validated before BuildAsync."),
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
