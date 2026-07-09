using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;

namespace LupiraCalApi.Mappers;

/// <summary>Builds the composable <see cref="ItemDetails"/> carrier from the request — resolving <c>TravelLegRequest</c>
/// free-text places to a LupiraGeoApi place id + denormalized label (via <see cref="IGeoResolver"/>) — and merges a
/// partial update member-by-member.</summary>
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
        ItemDetailsRequest? d, AvailabilityStatus? availability, IGeoResolver geo, CancellationToken ct)
    {
        TravelLeg? travel = null;
        if (d?.Travel is { } t)
        {
            var (toId, toLabel) = await ResolveAsync(geo, t.ToPlace, ct);
            var (fromId, fromLabel) = await ResolveAsync(geo, t.FromPlace, ct);
            travel = new TravelLeg(
                t.Mode, toId, fromId, t.DepartAt, t.ArriveAt, t.Carrier, t.ServiceNumber,
                t.DeparturePoint, t.ArrivalPoint, t.Seat, t.DriverContactId, toLabel, fromLabel);
        }

        var booking = d?.Booking;
        var presence = availability is { } s ? new PresenceDetail(s) : null;

        if (booking is null && travel is null && presence is null) return null;
        return new ItemDetails(booking, travel, presence);
    }

    /// <summary>Resolve free-text to a (geo place id, label); geo off/unresolved ⇒ (null, trimmed text).</summary>
    private static async Task<(Guid? Id, string? Label)> ResolveAsync(IGeoResolver geo, string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        if (geo.IsConfigured && await geo.ResolveAsync(text, ct) is { } r) return (r.PlaceId, r.Name);
        return (null, text.Trim());
    }

    /// <summary>Member-level merge: each populated member of <paramref name="incoming"/> replaces the same member on
    /// <paramref name="existing"/>; omitted members are kept. (A member is replaced wholesale, not field-merged.)</summary>
    public static ItemDetails Merge(ItemDetails? existing, ItemDetails incoming) =>
        existing is null ? incoming : new ItemDetails(
            incoming.Booking ?? existing.Booking,
            incoming.Travel ?? existing.Travel,
            incoming.Presence ?? existing.Presence);
}
