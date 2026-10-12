using LupiraCalApi.Core.Abstractions;
using LupiraCalApi.Core.Application.Places;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.Places;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Aggregation rules of <see cref="PlaceUsageService.Aggregate"/>: occurrence counting, per-category tallies,
/// travel endpoints, cancelled exclusion, the window cap on recurrences, survivor folding, companions, and ordering.</summary>
public class PlaceUsageTests
{
    private static readonly RecurrenceExpander Expander = new();
    private static readonly DateTimeOffset From = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar3 = new(2025, 3, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cafe = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();

    private static PlaceUsageRow Row(Guid? placeId, DateTimeOffset start, ItemCategory? category = ItemCategory.Meal, string? rule = null, string? zone = "UTC") => new()
    {
        Status = ItemStatus.Confirmed,
        Category = category,
        PlaceId = placeId,
        LocationLabel = "Somewhere",
        StartsAt = start,
        StartTimezone = zone,
        RecurrenceRule = rule,
    };

    private static List<PlaceUsageDto> Aggregate(IEnumerable<PlaceUsageRow> rows, IReadOnlyDictionary<Guid, GeoPlaceSummary>? places = null, int limit = 200) =>
        PlaceUsageService.Aggregate(rows, places, From, To, limit, Expander);

    private static List<ItemAttendee> Attendees(params Guid[] contactIds) =>
        [.. contactIds.Select(c => new ItemAttendee { ParticipationId = Guid.NewGuid(), ContactId = c })];

    [Fact]
    public void Counts_occurrences_per_category_with_each_categorys_last_day()
    {
        var usage = Assert.Single(Aggregate(
        [
            Row(Cafe, Mar3),
            Row(Cafe, Mar3.AddDays(10)),
            Row(Cafe, Mar3.AddDays(5), ItemCategory.Meeting),
            Row(Cafe, Mar3.AddDays(1), category: null),
        ]));

        Assert.Equal((Cafe, 4, new DateOnly(2025, 3, 13)), (usage.PlaceId, usage.EventCount, usage.LastDay));
        Assert.Equal(
            [(ItemCategory.Meal, 2, new DateOnly(2025, 3, 13)), (ItemCategory.Meeting, 1, new DateOnly(2025, 3, 8)), (ItemCategory.General, 1, new DateOnly(2025, 3, 4))],
            usage.ByCategory.Select(c => (c.Category, c.EventCount, c.LastDay)));
    }

    [Fact]
    public void Last_day_is_the_occurrences_local_date()
    {
        var lateEvening = new DateTimeOffset(2025, 3, 3, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2025, 3, 4), Assert.Single(Aggregate([Row(Cafe, lateEvening, zone: "Europe/Stockholm")])).LastDay);
        Assert.Equal(new DateOnly(2025, 3, 3), Assert.Single(Aggregate([Row(Cafe, lateEvening)])).LastDay);
    }

    [Fact]
    public void A_trip_counts_at_both_travel_endpoints_with_their_labels()
    {
        var (arlanda, kastrup) = (Guid.NewGuid(), Guid.NewGuid());
        var trip = Row(null, Mar3, ItemCategory.Trip);
        (trip.FromPlaceId, trip.FromLabel, trip.ToPlaceId, trip.ToLabel) = (arlanda, "Arlanda", kastrup, "Kastrup");

        var usage = Aggregate([trip]).ToDictionary(u => u.PlaceId);

        Assert.Equal(2, usage.Count);
        Assert.Equal(("Arlanda", 1), (usage[arlanda].Label, usage[arlanda].EventCount));
        Assert.Equal(("Kastrup", 1), (usage[kastrup].Label, usage[kastrup].EventCount));
        Assert.Equal(ItemCategory.Trip, Assert.Single(usage[kastrup].ByCategory).Category);
    }

    [Fact]
    public void A_place_referenced_twice_by_one_item_counts_once()
    {
        var row = Row(Cafe, Mar3);
        row.ToPlaceId = Cafe;

        Assert.Equal(1, Assert.Single(Aggregate([row])).EventCount);
    }

    [Fact]
    public void Cancelled_items_and_placeless_items_do_not_count()
    {
        var cancelled = Row(Office, Mar3);
        cancelled.Status = ItemStatus.Cancelled;

        Assert.Equal(Cafe, Assert.Single(Aggregate([cancelled, Row(null, Mar3), Row(Cafe, Mar3)])).PlaceId);
    }

    [Fact]
    public void A_recurrence_counts_only_occurrences_inside_the_window()
    {
        var weekly = Row(Cafe, new DateTimeOffset(2024, 12, 2, 12, 0, 0, TimeSpan.Zero), rule: "FREQ=WEEKLY");

        var usage = Assert.Single(Aggregate([weekly]));

        Assert.Equal(52, usage.EventCount);
        Assert.Equal(new DateOnly(2025, 12, 29), usage.LastDay);
    }

    [Fact]
    public void Items_outside_the_window_do_not_count()
    {
        Assert.Empty(Aggregate([Row(Cafe, From.AddDays(-1)), Row(Cafe, To)]));
    }

    [Fact]
    public void Merged_ids_fold_into_the_survivor_under_its_geo_name()
    {
        var mergedAway = Guid.NewGuid();
        var places = new Dictionary<Guid, GeoPlaceSummary>
        {
            [Cafe] = new(Cafe, "Café Linnea", 59.3, 18.0),
            [mergedAway] = new(Cafe, "Café Linnea", 59.3, 18.0),
        };

        var usage = Assert.Single(Aggregate([Row(Cafe, Mar3), Row(mergedAway, Mar3.AddDays(1)), Row(mergedAway, Mar3.AddDays(2))], places));

        Assert.Equal((Cafe, "Café Linnea", 3), (usage.PlaceId, usage.Label, usage.EventCount));
    }

    [Fact]
    public void Without_geo_ids_stay_unfolded_and_label_comes_from_the_latest_item()
    {
        var older = Row(Cafe, Mar3);
        older.LocationLabel = "Old name";
        var newer = Row(Cafe, Mar3.AddDays(1));
        newer.LocationLabel = "New name";

        Assert.Equal("New name", Assert.Single(Aggregate([newer, older])).Label);
        Assert.Equal(2, Aggregate([Row(Cafe, Mar3), Row(Office, Mar3)], places: null).Count);
    }

    [Fact]
    public void Companions_are_the_top_five_present_attendees_by_occurrences()
    {
        var c = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        var weekly = Row(Cafe, Mar3, rule: "FREQ=WEEKLY;COUNT=3");
        weekly.Attendees = Attendees(c[0], c[1]);
        var once = Row(Cafe, Mar3.AddDays(1));
        once.Attendees = Attendees(c[0], c[2], c[3], c[4], c[5], c[6]);
        once.Attendees[^1].LeftAt = Mar3;
        once.Attendees[^2].Status = ParticipationStatus.Declined;

        var companions = Assert.Single(Aggregate([weekly, once])).Companions;

        Assert.Equal(5, companions.Count);
        Assert.Equal((c[0], 4), (companions[0].ContactId, companions[0].EventCount));
        Assert.Equal((c[1], 3), (companions[1].ContactId, companions[1].EventCount));
        Assert.DoesNotContain(companions, x => x.ContactId == c[5] || x.ContactId == c[6]);
    }

    [Fact]
    public void Orders_by_last_day_then_event_count_and_applies_the_limit()
    {
        var park = Guid.NewGuid();
        List<PlaceUsageRow> rows =
        [
            Row(Office, Mar3),
            Row(Cafe, Mar3.AddDays(5)),
            Row(park, Mar3.AddDays(5)),
            Row(park, Mar3),
        ];

        Assert.Equal([park, Cafe, Office], Aggregate(rows).Select(u => u.PlaceId));
        Assert.Equal([park, Cafe], Aggregate(rows, limit: 2).Select(u => u.PlaceId));
    }
}
