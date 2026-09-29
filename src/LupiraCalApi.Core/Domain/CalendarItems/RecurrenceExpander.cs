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

        var starts = new SortedSet<DateTimeOffset>(SeriesStarts(RecurringSeries.Of(item), windowStart, windowEnd));
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

    /// <summary>Whether the unmodified series (or one of its extra occurrences) starts an occurrence at
    /// <paramref name="start"/> — the identity a per-occurrence change keys on.</summary>
    public bool HasSeriesOccurrence(CalendarItem item, DateTimeOffset start) =>
        (item.ExtraOccurrences ?? []).Contains(start) || Generates(RecurringSeries.Of(item), start);

    /// <summary>Whether <paramref name="series"/> itself — extra occurrences aside — starts an occurrence at
    /// <paramref name="start"/>.</summary>
    public bool Generates(RecurringSeries series, DateTimeOffset start) => SeriesStarts(series, start, start.AddTicks(1)).Any();

    private static IEnumerable<DateTimeOffset> SeriesStarts(RecurringSeries series, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        if (series.Anchor is not { } anchor) yield break;
        var anchorAt = anchor.At.ToDateTimeOffset();

        if (!series.Recurs)
        {
            if (anchorAt >= windowStart && anchorAt < windowEnd) yield return anchorAt;
            yield break;
        }

        var starts = RecurrenceRuleEvaluator.Evaluate(
            series.RecurrenceRule, anchor.At, anchor.Zone, Instant.FromDateTimeOffset(windowStart), series.IsAllDay);
        foreach (var instant in starts)
        {
            var start = instant.ToDateTimeOffset();
            if (start >= windowEnd) yield break;
            if (start >= windowStart) yield return start;
        }
    }
}
