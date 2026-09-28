namespace LupiraCalApi.Core.Application.Hotspots;

/// <summary>
/// DBSCAN where density is distinct days, not points: a point is core when the union of days across its
/// <see cref="NeighbourRadiusM"/> neighbourhood reaches <c>minDays</c>. A cluster wider than <see cref="MaxRadiusM"/> (a
/// chain through a town) is re-clustered at twice the threshold so its dense cores come out as separate hotspots.
/// </summary>
internal static class HotspotClusterer
{
    public const double NeighbourRadiusM = 150;
    public const double MaxRadiusM = 1000;
    private const int MaxSplitRounds = 3;
    private const int Unvisited = 0;
    private const int Noise = -1;
    private const double EarthRadiusM = 6_371_008.8;

    // The shortest metres-per-degree on the ellipsoid: grid distances never exceed true ones, so ±1 cell finds every neighbour.
    private const double MinMetresPerDegree = 110_540;

    public static List<HotspotCluster> Cluster(IReadOnlyList<DensityPoint> points, int minDays) => Cluster(points, minDays, 0);

    public static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static List<HotspotCluster> Cluster(IReadOnlyList<DensityPoint> points, int minDays, int round)
    {
        var neighbours = NeighbourIndex(points);
        var labels = new int[points.Count];
        var clusterCount = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (labels[i] != Unvisited) continue;
            var seeds = neighbours(i);
            if (!IsCore(points, seeds, minDays))
            {
                labels[i] = Noise;
                continue;
            }

            labels[i] = ++clusterCount;
            var frontier = new Queue<int>(seeds);
            while (frontier.TryDequeue(out var j))
            {
                if (labels[j] == Noise) labels[j] = clusterCount;
                if (labels[j] != Unvisited) continue;
                labels[j] = clusterCount;
                var reach = neighbours(j);
                if (IsCore(points, reach, minDays))
                    foreach (var k in reach) frontier.Enqueue(k);
            }
        }

        var clusters = new List<HotspotCluster>();
        foreach (var group in Enumerable.Range(0, points.Count).Where(i => labels[i] > 0).GroupBy(i => labels[i]))
        {
            var cluster = HotspotCluster.From([.. group.Select(i => points[i])]);
            var split = cluster.RadiusM > MaxRadiusM && round < MaxSplitRounds ? Cluster(cluster.Members, minDays * 2, round + 1) : [];
            if (split.Count > 0) clusters.AddRange(split);
            else clusters.Add(cluster);
        }

        return clusters;
    }

    private static bool IsCore(IReadOnlyList<DensityPoint> points, List<int> neighbourhood, int minDays)
    {
        var days = new HashSet<DateOnly>();
        foreach (var i in neighbourhood)
        {
            days.UnionWith(points[i].Days);
            if (days.Count >= minDays) return true;
        }

        return false;
    }

    private static Func<int, List<int>> NeighbourIndex(IReadOnlyList<DensityPoint> points)
    {
        var widestLat = points.Count == 0 ? 0 : Math.Min(85, points.Max(p => Math.Abs(p.Latitude)));
        var lonMetres = MinMetresPerDegree * Math.Cos(ToRadians(widestLat));
        (long Row, long Col) CellOf(DensityPoint p) =>
            ((long) Math.Floor(p.Latitude * MinMetresPerDegree / NeighbourRadiusM), (long) Math.Floor(p.Longitude * lonMetres / NeighbourRadiusM));

        var grid = new Dictionary<(long Row, long Col), List<int>>();
        for (var i = 0; i < points.Count; i++)
        {
            var cell = CellOf(points[i]);
            if (!grid.TryGetValue(cell, out var bucket)) grid[cell] = bucket = [];
            bucket.Add(i);
        }

        return i =>
        {
            var p = points[i];
            var (row, col) = CellOf(p);
            var found = new List<int>();
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                    if (grid.TryGetValue((row + dr, col + dc), out var bucket))
                        found.AddRange(bucket.Where(j => DistanceM(p.Latitude, p.Longitude, points[j].Latitude, points[j].Longitude) <= NeighbourRadiusM));
            return found;
        };
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
