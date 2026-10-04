using System.Globalization;
using System.Text.RegularExpressions;
using Lupira.Results;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.Hotspots;
using Marten;

namespace LupiraCalApi.Core.Application.Hotspots;

/// <summary>
/// Places where the caller's life concentrates, derived at read time (never stored) from event occurrences in calendars
/// they can read plus their own photos. Weight is distinct UTC days, so a 300-photo wedding counts once and a weekly class
/// counts every week. A hotspot anchors to the geo place most of its event days reference, else to a place within
/// <see cref="AnchorRadiusM"/>, else it carries a reverse-geocoded label.
/// </summary>
public sealed partial class HotspotService(IQuerySession session, AccessResolver access, RecurrenceExpander expander, IGeoResolver geo, IPhotoDensitySource photos)
{
    public const int DefaultMinDays = 3;
    public const int DefaultLimit = 100;
    private const int MaxMinDays = 365;
    private const int MaxLimit = 500;
    private const int MaxSpanDays = 31;
    private const int AnchorRadiusM = 75;
    private const int GeoConcurrency = 4;

    // Reverse geocoding may fall back to a public geocoder rate-gated at 1 req/s.
    private const int MaxGeoLabelled = 30;

    public async Task<OpResult<List<HotspotDto>>> ListAsync(
        Guid principalId, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, int? minDays, int? limit, CancellationToken ct = default)
    {
        var windowStart = from ?? DateTimeOffset.MinValue;
        var windowEnd = to ?? DateTimeOffset.UtcNow;
        if (windowStart >= windowEnd) return OpResult<List<HotspotDto>>.Invalid("from must be before to.");
        var threshold = minDays ?? DefaultMinDays;
        if (threshold is < 1 or > MaxMinDays) return OpResult<List<HotspotDto>>.Invalid($"minDays must be between 1 and {MaxMinDays}.");
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit) return OpResult<List<HotspotDto>>.Invalid($"limit must be between 1 and {MaxLimit}.");

        var scope = await access.AccessibleCalendarIdsAsync(principalId, ct);
        if (calendarId is { } cid)
        {
            if (!scope.Contains(cid)) return OpResult<List<HotspotDto>>.Forbidden("No access to this calendar.");
            scope = [cid];
        }

        var cellsTask = photos.CellsAsync(from, windowEnd, ct);
        var (eventPoints, placeNames) = await EventPointsAsync(scope, windowStart, windowEnd, ct);
        var cells = await cellsTask ?? [];
        List<DensityPoint> points =
        [
            .. eventPoints,
            .. cells.Select(c => new DensityPoint { Latitude = c.Latitude, Longitude = c.Longitude, PhotoCount = c.Count, Days = c.Days.ToHashSet() }),
        ];

        var ranked = HotspotClusterer.Cluster(points, threshold)
            .OrderByDescending(c => c.Days.Count).ThenByDescending(c => c.EventCount + c.PhotoCount)
            .Take(take)
            .ToList();
        return OpResult<List<HotspotDto>>.Ok(await AnchorAsync(ranked, placeNames, ct));
    }

    private async Task<(List<DensityPoint> Points, Dictionary<Guid, string> Names)> EventPointsAsync(
        List<Guid> scope, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        var byPlace = new Dictionary<Guid, PlaceTally>();
        foreach (var i in candidates)
        {
            if (i.Status == ItemStatus.Cancelled || (i.PlaceId ?? i.Details?.Travel?.ToPlaceId) is not { } placeId) continue;
            if (!i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && scope.Contains(m.CalendarId))) continue;
            var starts = OccurrenceStarts(i, windowStart, windowEnd);
            if (starts.Count == 0) continue;

            var tally = byPlace.TryGetValue(placeId, out var t) ? t : byPlace[placeId] = new PlaceTally();
            tally.Occurrences += starts.Count;
            var span = SpanDays(i);
            foreach (var start in starts)
            {
                var first = DateOnly.FromDateTime(start.UtcDateTime);
                for (var d = 0; d < span; d++) tally.Days.Add(first.AddDays(d));
            }
        }

        if (byPlace.Count == 0 || await geo.LookupAsync(byPlace.Keys, ct) is not { } resolved) return ([], []);

        var bySurvivor = new Dictionary<Guid, (GeoPlaceSummary Place, PlaceTally Tally)>();
        foreach (var (requestedId, tally) in byPlace)
        {
            if (!resolved.TryGetValue(requestedId, out var place) || place.Latitude is null || place.Longitude is null) continue;
            if (bySurvivor.TryGetValue(place.PlaceId, out var existing))
            {
                existing.Tally.Occurrences += tally.Occurrences;
                existing.Tally.Days.UnionWith(tally.Days);
            }
            else
            {
                bySurvivor[place.PlaceId] = (place, tally);
            }
        }

        var points = bySurvivor.Values.Select(v => new DensityPoint
        {
            Latitude = v.Place.Latitude!.Value,
            Longitude = v.Place.Longitude!.Value,
            PlaceId = v.Place.PlaceId,
            EventCount = v.Tally.Occurrences,
            Days = v.Tally.Days,
        }).ToList();
        return (points, bySurvivor.ToDictionary(kv => kv.Key, kv => kv.Value.Place.Name));
    }

    private IReadOnlyList<DateTimeOffset> OccurrenceStarts(CalendarItem i, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        if (!string.IsNullOrWhiteSpace(i.RecurrenceRule)) return expander.Expand(i, windowStart, windowEnd);
        return CalendarItemService.OccurrenceStart(i) is { } start && start >= windowStart && start < windowEnd ? [start] : [];
    }

    private static int SpanDays(CalendarItem i)
    {
        // All-day EndDate is the inclusive last day, as in SearchAsync.
        var days = i.IsAllDay && i.StartDate is { } sd && i.EndDate is { } ed ? ed.DayNumber - sd.DayNumber + 1
            : i.StartsAt is { } s && i.EndsAt is { } e && e > s
                ? DateOnly.FromDateTime(e.UtcDateTime.AddTicks(-1)).DayNumber - DateOnly.FromDateTime(s.UtcDateTime).DayNumber + 1
                : 1;
        return Math.Clamp(days, 1, MaxSpanDays);
    }

    private async Task<List<HotspotDto>> AnchorAsync(List<HotspotCluster> ranked, Dictionary<Guid, string> placeNames, CancellationToken ct)
    {
        var claimed = new HashSet<Guid>();
        var anchors = new (Guid PlaceId, string Name)?[ranked.Count];
        for (var i = 0; i < ranked.Count; i++)
        {
            var own = ranked[i].Members
                .Where(m => m.PlaceId is { } p && !claimed.Contains(p))
                .OrderByDescending(m => m.Days.Count).ThenByDescending(m => m.EventCount)
                .FirstOrDefault();
            if (own?.PlaceId is { } placeId && claimed.Add(placeId)) anchors[i] = (placeId, placeNames[placeId]);
        }

        var labels = new string?[ranked.Count];
        if (geo.IsConfigured)
        {
            var open = Enumerable.Range(0, ranked.Count).Where(i => anchors[i] is null).Take(MaxGeoLabelled).ToList();
            var options = new ParallelOptions { MaxDegreeOfParallelism = GeoConcurrency, CancellationToken = ct };

            var nearest = new GeoPlaceSummary?[ranked.Count];
            await Parallel.ForEachAsync(open, options, async (i, token) =>
                nearest[i] = await geo.NearestAsync(ranked[i].Latitude, ranked[i].Longitude, AnchorRadiusM, token));
            foreach (var i in open)
                if (nearest[i] is { } place && claimed.Add(place.PlaceId)) anchors[i] = (place.PlaceId, place.Name);

            await Parallel.ForEachAsync(open.Where(i => anchors[i] is null), options, async (i, token) =>
                labels[i] = ShortLabel(await geo.ReverseAsync(ranked[i].Latitude, ranked[i].Longitude, token)));
        }

        return [.. ranked.Select((c, i) => ToDto(c, anchors[i], labels[i]))];
    }

    private static HotspotDto ToDto(HotspotCluster c, (Guid PlaceId, string Name)? anchor, string? label)
    {
        var densest = c.Members.MaxBy(m => m.Days.Count)!;
        return new HotspotDto
        {
            Id = anchor is { } a
                ? $"place:{a.PlaceId}"
                : string.Create(CultureInfo.InvariantCulture, $"cell:{densest.Latitude:F3},{densest.Longitude:F3}"),
            Latitude = Math.Round(c.Latitude, 6),
            Longitude = Math.Round(c.Longitude, 6),
            RadiusM = (int) Math.Round(c.RadiusM),
            PlaceId = anchor?.PlaceId,
            Label = anchor?.Name ?? label,
            EventCount = c.EventCount,
            PhotoCount = c.PhotoCount,
            ActiveDays = c.Days.Count,
            FirstDay = c.Days.Min(),
            LastDay = c.Days.Max(),
        };
    }

    /// <summary>"Kyrkogatan 5, Ljungby" from a geocoder display name, which leads with the house number when there's no venue name.</summary>
    internal static string? ShortLabel(GeoReverseLabel? reverse)
    {
        if (reverse is null) return null;
        var parts = reverse.DisplayName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return reverse.Locality;
        var head = parts.Length > 1 && HouseNumber().IsMatch(parts[0]) ? $"{parts[1]} {parts[0]}" : parts[0];
        return reverse.Locality is { Length: > 0 } town && !head.Contains(town, StringComparison.OrdinalIgnoreCase) ? $"{head}, {town}" : head;
    }

    [GeneratedRegex(@"^\d+\s?[A-Za-z]?$")]
    private static partial Regex HouseNumber();

    private sealed class PlaceTally
    {
        public int Occurrences { get; set; }

        public HashSet<DateOnly> Days { get; } = [];
    }
}
