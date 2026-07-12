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

    /// <summary>Resolve the request into a details carrier. <c>Details</c> is null when nothing is set (so the caller preserves
    /// existing details). <c>Unresolved</c> is true when a travel place had text but geo (configured) couldn't resolve it — a
    /// retryable failure the REST/MCP callers reject. Callers must <see cref="Validate"/> first.</summary>
    public static async Task<(ItemDetails? Details, bool Unresolved)> BuildAsync(
        ItemDetailsRequest? d, AvailabilityStatus? availability, IGeoResolver geo, CancellationToken ct)
    {
        TravelLeg? travel = null;
        if (d?.Travel is { } t)
        {
            var (toId, toLabel, toUnresolved) = await ResolveAsync(geo, t.ToPlace, ct);
            var (fromId, fromLabel, fromUnresolved) = await ResolveAsync(geo, t.FromPlace, ct);
            if (toUnresolved || fromUnresolved) return (null, true);
            travel = new TravelLeg(
                t.Mode, toId, fromId, t.DepartAt, t.ArriveAt, t.Carrier, t.ServiceNumber,
                t.DeparturePoint, t.ArrivalPoint, t.Seat, t.DriverContactId, toLabel, fromLabel);
        }

        var booking = d?.Booking;
        var presence = availability is { } s ? new PresenceDetail(s) : null;

        if (booking is null && travel is null && presence is null) return (null, false);
        return (new ItemDetails(booking, travel, presence), false);
    }

    /// <summary>Resolve free-text to a (geo place id, label). <c>Unresolved</c> true only when geo is configured but couldn't
    /// resolve (retryable); unconfigured (dev/test) degrades to the trimmed label without error.</summary>
    private static async Task<(Guid? Id, string? Label, bool Unresolved)> ResolveAsync(IGeoResolver geo, string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null, false);
        if (!geo.IsConfigured) return (null, text.Trim(), false);
        if (await geo.ResolveAsync(text, ct) is { } r) return (r.PlaceId, r.Name, false);
        return (null, text.Trim(), true);
    }

    /// <summary>Member-level merge: each populated member of <paramref name="incoming"/> replaces the same member on
    /// <paramref name="existing"/>; omitted members are kept. (A member is replaced wholesale, not field-merged.)</summary>
    public static ItemDetails Merge(ItemDetails? existing, ItemDetails incoming) =>
        existing is null ? incoming : new ItemDetails(
            incoming.Booking ?? existing.Booking,
            incoming.Travel ?? existing.Travel,
            incoming.Presence ?? existing.Presence);
}
