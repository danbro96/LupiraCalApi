using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;

namespace LupiraCalApi.Mappers;

/// <summary>Builds the composable <see cref="ItemDetails"/> carrier from the request — resolving <see cref="TravelLegRequest"/>
/// free-text place labels to <see cref="Place"/> ids (via <see cref="PlaceService"/>) — and merges a partial update
/// member-by-member.</summary>
internal static class ItemDetailsMapper
{
    /// <summary>Request-level consistency; null = valid. <c>Travel</c> applies only to a <c>Trip</c> and needs a destination;
    /// <c>Booking</c> is valid on any category; a presence segment (top-level Availability) is unrestricted. Deliberately does
    /// NOT judge resulting completeness — a partial member is legal (progressive enrichment; the scorer surfaces the gaps).</summary>
    public static string? Validate(ItemCategory? category, ItemDetailsRequest? details)
    {
        if (details?.Travel is { } t)
        {
            if (category != ItemCategory.Trip)
                return $"Travel detail applies only to category 'Trip', not '{Name(category)}'.";
            if (string.IsNullOrWhiteSpace(t.ToPlace))
                return "Travel.ToPlace is required (Travel is replaced wholesale — resend the full member including ToPlace).";
        }
        return null;
    }

    private static string Name(ItemCategory? c) => c?.ToString() ?? "none";

    /// <summary>Resolve the request into a details carrier. Returns null when nothing is set (so the caller preserves existing
    /// details). Callers must <see cref="Validate"/> first.</summary>
    public static async Task<ItemDetails?> BuildAsync(
        ItemDetailsRequest? d, AvailabilityStatus? availability, PlaceService places, CancellationToken ct)
    {
        TravelLeg? travel = null;
        if (d?.Travel is { } t)
            travel = new TravelLeg(
                t.Mode,
                await places.ResolveLabelAsync(t.ToPlace, ct)
                    ?? throw new InvalidOperationException("Travel.ToPlace must be validated before BuildAsync."),
                await places.ResolveLabelAsync(t.FromPlace, ct),
                t.DepartAt, t.ArriveAt, t.Carrier, t.ServiceNumber, t.DeparturePoint, t.ArrivalPoint, t.Seat, t.DriverContactId);

        var booking = d?.Booking;
        var presence = availability is { } s ? new PresenceDetail(s) : null;

        if (booking is null && travel is null && presence is null) return null;
        return new ItemDetails(booking, travel, presence);
    }

    /// <summary>Member-level merge: each populated member of <paramref name="incoming"/> replaces the same member on
    /// <paramref name="existing"/>; omitted members are kept. (A member is replaced wholesale, not field-merged.)</summary>
    public static ItemDetails Merge(ItemDetails? existing, ItemDetails incoming) =>
        existing is null ? incoming : new ItemDetails(
            incoming.Booking ?? existing.Booking,
            incoming.Travel ?? existing.Travel,
            incoming.Presence ?? existing.Presence);
}
