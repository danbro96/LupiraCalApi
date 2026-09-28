namespace LupiraCalApi.Core.Application.Hotspots;

/// <summary>A density cluster: its members, day-weighted centre, reach and the union of their days.</summary>
internal sealed class HotspotCluster
{
    public required IReadOnlyList<DensityPoint> Members { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required double RadiusM { get; init; }

    public required IReadOnlySet<DateOnly> Days { get; init; }

    public int EventCount => Members.Sum(m => m.EventCount);

    public int PhotoCount => Members.Sum(m => m.PhotoCount);

    public static HotspotCluster From(IReadOnlyList<DensityPoint> members)
    {
        double weight = members.Sum(m => m.Days.Count);
        var lat = members.Sum(m => m.Latitude * m.Days.Count) / weight;
        var lon = members.Sum(m => m.Longitude * m.Days.Count) / weight;
        var days = new HashSet<DateOnly>();
        foreach (var m in members) days.UnionWith(m.Days);
        return new HotspotCluster
        {
            Members = members,
            Latitude = lat,
            Longitude = lon,
            RadiusM = members.Max(m => HotspotClusterer.DistanceM(lat, lon, m.Latitude, m.Longitude)),
            Days = days,
        };
    }
}
