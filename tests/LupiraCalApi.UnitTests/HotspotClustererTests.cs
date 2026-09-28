using LupiraCalApi.Core.Application.Hotspots;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Day-weighted DBSCAN: density is distinct days across a 150 m neighbourhood, and chains wider than 1 km split
/// into their dense cores.</summary>
public class HotspotClustererTests
{
    private const double Lat = 59.33;
    private const double Lon = 18.07;
    private const double HundredMetresLat = 0.0009;
    private static readonly DateOnly Day0 = new(2026, 1, 1);

    private static HashSet<DateOnly> Days(int first, int count) => [.. Enumerable.Range(first, count).Select(Day0.AddDays)];

    private static DensityPoint Place(double lat, double lon, HashSet<DateOnly> days) =>
        new() { Latitude = lat, Longitude = lon, PlaceId = Guid.NewGuid(), EventCount = days.Count, Days = days };

    private static DensityPoint Photos(double lat, double lon, int count, HashSet<DateOnly> days) =>
        new() { Latitude = lat, Longitude = lon, PhotoCount = count, Days = days };

    [Fact]
    public void A_place_with_enough_days_is_a_hotspot()
    {
        var cluster = Assert.Single(HotspotClusterer.Cluster([Place(Lat, Lon, Days(0, 3))], 3));
        Assert.Equal(3, cluster.Days.Count);
        Assert.Equal(0, cluster.RadiusM, 3);
    }

    [Fact]
    public void Too_few_days_is_noise() =>
        Assert.Empty(HotspotClusterer.Cluster([Place(Lat, Lon, Days(0, 2))], 3));

    [Fact]
    public void A_photo_burst_on_one_day_counts_once() =>
        Assert.Empty(HotspotClusterer.Cluster(
            [Photos(Lat, Lon, 300, Days(0, 1)), Photos(Lat + HundredMetresLat, Lon, 40, Days(0, 1))], 3));

    [Fact]
    public void Photo_cells_within_reach_merge_with_the_event_place()
    {
        var cluster = Assert.Single(HotspotClusterer.Cluster(
            [Place(Lat, Lon, Days(0, 2)), Photos(Lat + HundredMetresLat, Lon, 12, Days(5, 2))], 3));
        Assert.Equal(4, cluster.Days.Count);
        Assert.Equal(2, cluster.EventCount);
        Assert.Equal(12, cluster.PhotoCount);
    }

    [Fact]
    public void Places_farther_apart_than_the_radius_stay_separate() =>
        Assert.Equal(2, HotspotClusterer.Cluster(
            [Place(Lat, Lon, Days(0, 3)), Place(Lat + 4 * HundredMetresLat, Lon, Days(0, 3))], 3).Count);

    [Fact]
    public void A_chain_wider_than_a_kilometre_splits_into_its_dense_cores()
    {
        const int links = 31;
        var chain = Enumerable.Range(0, links).Select(i => i switch
        {
            0 => Photos(Lat, Lon, 50, Days(100, 10)),
            links - 1 => Photos(Lat + i * HundredMetresLat, Lon, 50, Days(200, 10)),
            _ => Photos(Lat + i * HundredMetresLat, Lon, 1, Days(i, 1)),
        }).ToList();

        var clusters = HotspotClusterer.Cluster(chain, 3);

        Assert.Equal(2, clusters.Count);
        Assert.All(clusters, c => Assert.True(c.RadiusM < HotspotClusterer.MaxRadiusM));
        Assert.Contains(clusters, c => c.Members.Contains(chain[0]));
        Assert.Contains(clusters, c => c.Members.Contains(chain[links - 1]));
    }

    [Fact]
    public void Empty_input_yields_nothing() => Assert.Empty(HotspotClusterer.Cluster([], 3));

    [Fact]
    public void Distance_is_haversine_metres() =>
        Assert.Equal(100, HotspotClusterer.DistanceM(Lat, Lon, Lat + HundredMetresLat, Lon), 0);
}
