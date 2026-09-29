using LupiraCalApi.Core.Domain.Shared;
using NodaTime;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>
/// Expands an item into concrete occurrence starts (UTC) within a half-open window [start, end); an item without a rule
/// has its one start. A timed series recurs at the same wall-clock time in <see cref="CalendarItem.StartTimezone"/> (UTC when
/// absent or unknown), so it keeps local time across offset changes: a start that falls in a forward shift moves later by
/// the gap, one that repeats in a backward shift takes the earlier instant. An all-day series recurs on dates, reported at
/// 00:00Z. The item's per-occurrence deviations apply last. One expander backs every surface (REST/MCP, time-range queries,
/// hotspots, fires).
/// </summary>
public sealed class RecurrenceExpander
{
    public IReadOnlyList<DateTimeOffset> Expand(CalendarItem item, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        if (windowEnd <= windowStart) return [];
        bool InWindow(DateTimeOffset s) => s >= windowStart && s < windowEnd;

        var starts = new SortedSet<DateTimeOffset>(SeriesStarts(item, item.RecurrenceRule, windowStart, windowEnd));
        foreach (var s in item.ExcludedOccurrences ?? []) starts.Remove(s);
        foreach (var s in item.ExtraOccurrences ?? [])
            if (InWindow(s)) starts.Add(s);
        foreach (var o in item.OccurrenceOverrides ?? [])
        {
            starts.Remove(o.OriginalStart);
            var moved = o.StartsAt ?? o.OriginalStart;
            if (o.Status != ItemStatus.Cancelled && InWindow(moved)) starts.Add(moved);
        }

        return [.. starts];
    }

    private static IEnumerable<DateTimeOffset> SeriesStarts(CalendarItem item, string? rule, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        DateTimeOffset anchorAt;
        DateTimeZone zone;
        if (item.IsAllDay)
        {
            if (item.StartDate is not { } d) yield break;
            (anchorAt, zone) = (new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero), DateTimeZone.Utc);
        }
        else
        {
            if (item.StartsAt is not { } s) yield break;
            (anchorAt, zone) = (s, ZoneOf(item.StartTimezone));
        }

        if (string.IsNullOrWhiteSpace(rule))
        {
            if (anchorAt >= windowStart && anchorAt < windowEnd) yield return anchorAt;
            yield break;
        }

        var anchorLocal = WallClock(anchorAt, zone);
        // Evaluate from a day before the window so a zone offset can't hide an occurrence at its edge; a window opening
        // before the series (e.g. an all-time search's MinValue) starts at the anchor itself.
        var from = windowStart <= anchorAt ? anchorLocal : WallClock(windowStart, zone).PlusDays(-1);
        foreach (var local in RecurrenceRuleEvaluator.Evaluate(rule, anchorLocal, from, item.IsAllDay))
        {
            var start = local.InZoneLeniently(zone).ToInstant().ToDateTimeOffset();
            if (start >= windowEnd) yield break;
            if (start >= windowStart) yield return start;
        }
    }

    private static LocalDateTime WallClock(DateTimeOffset at, DateTimeZone zone) => Instant.FromDateTimeOffset(at).InZone(zone).LocalDateTime;

    // Windows zone ids (sent by some clients) are mapped to their IANA equivalent.
    private static DateTimeZone ZoneOf(string? tz)
    {
        if (string.IsNullOrWhiteSpace(tz)) return DateTimeZone.Utc;
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tz);
        if (zone is null && TimeZoneInfo.TryConvertWindowsIdToIanaId(tz, out var iana)) zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(iana);
        return zone ?? DateTimeZone.Utc;
    }
}
