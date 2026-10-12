using Lupira.Results;
using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.Places;
using Marten;
using NodaTime;

namespace LupiraCalApi.Core.Application.Places;

/// <summary>
/// Places the caller's events have used, derived at read time from non-cancelled occurrences in calendars they can read.
/// An item uses its <c>PlaceId</c> and its travel endpoints; ids geo has merged away fold into their survivor.
/// </summary>
public sealed class PlaceUsageService(IQuerySession session, AccessResolver access, RecurrenceExpander expander, IGeoResolver geo, TimeProvider clock)
{
    public const int DefaultLimit = 200;
    public const int DefaultLookbackYears = 3;
    private const int MaxLimit = 500;
    private const int MaxCompanions = 5;

    public async Task<OpResult<List<PlaceUsageDto>>> ListAsync(
        Guid principalId, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, int? limit, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var windowStart = from ?? now.AddYears(-DefaultLookbackYears);
        var windowEnd = to ?? now;
        if (windowStart >= windowEnd) return OpResult<List<PlaceUsageDto>>.Invalid("from must be before to.");
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit) return OpResult<List<PlaceUsageDto>>.Invalid($"limit must be between 1 and {MaxLimit}.");

        var scope = await access.AccessibleCalendarIdsAsync(principalId, ct);
        if (calendarId is { } cid)
        {
            if (!scope.Contains(cid)) return OpResult<List<PlaceUsageDto>>.Forbidden("No access to this calendar.");
            scope = [cid];
        }

        var rows = await RowsAsync(scope, windowStart, windowEnd, ct);
        var ids = rows.SelectMany(r => new[] { r.PlaceId, r.FromPlaceId, r.ToPlaceId }).OfType<Guid>().Distinct().ToList();
        var places = ids.Count > 0 ? await geo.LookupAsync(ids, ct) : null;
        return OpResult<List<PlaceUsageDto>>.Ok(Aggregate(rows, places, windowStart, windowEnd, take, expander));
    }

    /// <summary>Folds rows into per-place usage over [<paramref name="from"/>, <paramref name="to"/>). <paramref name="places"/>
    /// is the geo lookup keyed by referenced id; null (geo unavailable) leaves ids unfolded and labels from the items.</summary>
    internal static List<PlaceUsageDto> Aggregate(
        IEnumerable<PlaceUsageRow> rows, IReadOnlyDictionary<Guid, GeoPlaceSummary>? places, DateTimeOffset from, DateTimeOffset to, int limit,
        RecurrenceExpander expander)
    {
        var byPlace = new Dictionary<Guid, PlaceTally>();
        foreach (var row in rows)
        {
            if (row.Status == ItemStatus.Cancelled) continue;
            var refs = PlaceRefs(row, places);
            if (refs.Count == 0) continue;
            var starts = expander.Expand(row.Schedule(), from, to);
            if (starts.Count == 0) continue;

            var lastDay = DayOf(starts[^1], row);
            var category = row.Category ?? ItemCategory.General;
            var companions = (row.Attendees ?? [])
                .Where(a => a.LeftAt is null && a.Status != ParticipationStatus.Declined)
                .Select(a => a.ContactId).Distinct().ToList();
            foreach (var (placeId, label) in refs)
            {
                var tally = byPlace.TryGetValue(placeId, out var t) ? t : byPlace[placeId] = new PlaceTally();
                tally.Add(starts.Count, lastDay, category, label, companions);
            }
        }

        return [.. byPlace
            .OrderByDescending(kv => kv.Value.LastDay).ThenByDescending(kv => kv.Value.EventCount).ThenBy(kv => kv.Key)
            .Take(limit)
            .Select(kv => ToDto(kv.Key, kv.Value))];
    }

    // The window test is a superset (a series may start before the window); Aggregate expands the exact occurrences.
    private async Task<IReadOnlyList<PlaceUsageRow>> RowsAsync(List<Guid> scope, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var fromDate = DateOnly.FromDateTime(windowStart.UtcDateTime);
        var toDate = DateOnly.FromDateTime(windowEnd.UtcDateTime);
        return await session.Query<CalendarItem>()
            .Where(i => i.DeletedAt == null
                && (i.Status == null || i.Status != ItemStatus.Cancelled)
                && (i.PlaceId != null || i.Details!.Travel!.ToPlaceId != null || i.Details!.Travel!.FromPlaceId != null)
                && i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && scope.Contains(m.CalendarId))
                && ((i.RecurrenceRule != null && (i.StartsAt < windowEnd || i.StartDate <= toDate))
                    || (i.StartsAt >= windowStart && i.StartsAt < windowEnd)
                    || (i.StartDate >= fromDate && i.StartDate <= toDate)))
            .Select(i => new PlaceUsageRow
            {
                Status = i.Status,
                Category = i.Category,
                PlaceId = i.PlaceId,
                LocationLabel = i.LocationLabel,
                FromPlaceId = i.Details!.Travel!.FromPlaceId,
                FromLabel = i.Details!.Travel!.FromLabel,
                ToPlaceId = i.Details!.Travel!.ToPlaceId,
                ToLabel = i.Details!.Travel!.ToLabel,
                IsAllDay = i.IsAllDay,
                StartsAt = i.StartsAt,
                StartDate = i.StartDate,
                StartTimezone = i.StartTimezone,
                RecurrenceRule = i.RecurrenceRule,
                ExcludedOccurrences = i.ExcludedOccurrences,
                ExtraOccurrences = i.ExtraOccurrences,
                OccurrenceOverrides = i.OccurrenceOverrides,
                Attendees = i.Attendees,
            })
            .ToListAsync(ct);
    }

    private static List<(Guid PlaceId, string? Label)> PlaceRefs(PlaceUsageRow row, IReadOnlyDictionary<Guid, GeoPlaceSummary>? places)
    {
        var refs = new List<(Guid PlaceId, string? Label)>();
        Add(row.PlaceId, row.LocationLabel);
        Add(row.FromPlaceId, row.FromLabel);
        Add(row.ToPlaceId, row.ToLabel);
        return refs;

        void Add(Guid? id, string? itemLabel)
        {
            if (id is not { } requested) return;
            var place = places?.GetValueOrDefault(requested);
            var placeId = place?.PlaceId ?? requested;
            if (refs.Exists(r => r.PlaceId == placeId)) return;
            refs.Add((placeId, place?.Name ?? (string.IsNullOrWhiteSpace(itemLabel) ? null : itemLabel)));
        }
    }

    private static DateOnly DayOf(DateTimeOffset start, PlaceUsageRow row) =>
        !row.IsAllDay && TimeZoneIds.Find(row.StartTimezone) is { } zone
            ? Instant.FromDateTimeOffset(start).InZone(zone).Date.ToDateOnly()
            : DateOnly.FromDateTime(start.UtcDateTime);

    private static PlaceUsageDto ToDto(Guid placeId, PlaceTally t) => new()
    {
        PlaceId = placeId,
        Label = t.Label,
        EventCount = t.EventCount,
        LastDay = t.LastDay,
        ByCategory = [.. t.Categories
            .OrderByDescending(c => c.Value.EventCount).ThenByDescending(c => c.Value.LastDay).ThenBy(c => c.Key)
            .Select(c => new PlaceCategoryUsageDto { Category = c.Key, EventCount = c.Value.EventCount, LastDay = c.Value.LastDay })],
        Companions = [.. t.Companions
            .OrderByDescending(c => c.Value).ThenBy(c => c.Key)
            .Take(MaxCompanions)
            .Select(c => new PlaceCompanionDto { ContactId = c.Key, EventCount = c.Value })],
    };

    private sealed class PlaceTally
    {
        private DateOnly _labelDay;

        public int EventCount { get; private set; }

        public DateOnly LastDay { get; private set; }

        public string? Label { get; private set; }

        public Dictionary<ItemCategory, (int EventCount, DateOnly LastDay)> Categories { get; } = [];

        public Dictionary<Guid, int> Companions { get; } = [];

        public void Add(int occurrences, DateOnly lastDay, ItemCategory category, string? label, IEnumerable<Guid> companions)
        {
            EventCount += occurrences;
            if (lastDay > LastDay) LastDay = lastDay;
            if (label is not null && (Label is null || lastDay >= _labelDay)) (Label, _labelDay) = (label, lastDay);

            var (count, last) = Categories.GetValueOrDefault(category);
            Categories[category] = (count + occurrences, lastDay > last ? lastDay : last);
            foreach (var c in companions) Companions[c] = Companions.GetValueOrDefault(c) + occurrences;
        }
    }
}
