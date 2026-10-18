using LupiraCalApi.Core.Domain.Shared;
using NodaTime;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>
/// Expands an item into concrete occurrence starts (UTC) within a half-open window [start, end); an item without a rule
/// has its one start. A timed series recurs at the same wall-clock time in <see cref="CalendarItem.StartTimezone"/> (UTC when
/// absent or unknown), so it keeps local time across offset changes: a start that falls in a forward shift moves later by
/// the gap, one that repeats in a backward shift takes the earlier instant. An all-day series recurs on dates, reported at
/// 00:00Z. The item's per-occurrence deviations apply last, each keyed by the start its occurrence has in the unmodified
/// series. One expander backs every surface (REST/MCP, time-range queries, hotspots, fires).
/// </summary>
public sealed class RecurrenceExpander
{
    public IReadOnlyList<DateTimeOffset> Expand(CalendarItem item, DateTimeOffset windowStart, DateTimeOffset windowEnd) =>
        [.. Occurrences(item, windowStart, windowEnd).Select(o => o.Start).Distinct()];

    /// <summary>The item's occurrences starting in [windowStart, windowEnd), ordered by start: the series' starts and its
    /// extra ones less the excluded, each with its change. Cancelled ones are left out; two that start together both stay.</summary>
    public IReadOnlyList<SeriesOccurrence> Occurrences(CalendarItem item, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        if (windowEnd <= windowStart) return [];
        bool InWindow(DateTimeOffset s) => s >= windowStart && s < windowEnd;

        var series = RecurringSeries.Of(item);
        var extras = item.ExtraOccurrences ?? [];
        var changes = (item.OccurrenceOverrides ?? []).DistinctBy(o => o.OriginalStart).ToDictionary(o => o.OriginalStart);
        var originals = new SortedSet<DateTimeOffset>(Starts(series, windowStart, windowEnd));
        originals.UnionWith(extras.Where(InWindow));
        // A change can move an occurrence into the window from outside it.
        originals.UnionWith(changes.Values
            .Where(c => !InWindow(c.OriginalStart) && c.StartsAt is { } moved && InWindow(moved))
            .Select(c => c.OriginalStart)
            .Where(k => extras.Contains(k) || Generates(series, k)));
        originals.ExceptWith(item.ExcludedOccurrences ?? []);

        return [.. originals
            .Select(o => changes.TryGetValue(o, out var change) ? new SeriesOccurrence(o, change.StartsAt ?? o, change) : new SeriesOccurrence(o, o, null))
            .Where(o => o.Change?.Status != ItemStatus.Cancelled && InWindow(o.Start))
            .OrderBy(o => o.Start).ThenBy(o => o.OriginalStart)];
    }

    /// <summary>When the item's last occurrence ends — an all-day one at 00:00Z after its inclusive last day. Null when it
    /// has no start, no occurrence left, or recurs without bound.</summary>
    public DateTimeOffset? LastEnd(CalendarItem item)
    {
        DateTimeOffset first, firstEnd;
        if (item.IsAllDay && item.StartDate is { } sd) (first, firstEnd) = (Midnight(sd), Midnight(item.EndDate ?? sd).AddDays(1));
        else if (!item.IsAllDay && item.StartsAt is { } s) (first, firstEnd) = (s, item.EndsAt ?? s);
        else return null;

        var series = RecurringSeries.Of(item);
        if (!series.Recurs) return firstEnd;
        if (!RecurrenceRuleEvaluator.IsBounded(series.RecurrenceRule)) return null;

        var length = firstEnd - first;
        return Occurrences(item, DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
            .Select(o => o.Change?.EndsAt is { } end ? (item.IsAllDay ? end.AddDays(1) : end) : o.Start + length)
            .Cast<DateTimeOffset?>()
            .Max();
    }

    /// <summary>Whether the unmodified series (or one of its extra occurrences) starts an occurrence at
    /// <paramref name="start"/> — the identity a per-occurrence change keys on.</summary>
    public bool HasSeriesOccurrence(CalendarItem item, DateTimeOffset start) =>
        (item.ExtraOccurrences ?? []).Contains(start) || Generates(RecurringSeries.Of(item), start);

    /// <summary>Whether <paramref name="series"/> itself — extra occurrences aside — starts an occurrence at
    /// <paramref name="start"/>.</summary>
    public bool Generates(RecurringSeries series, DateTimeOffset start) => Starts(series, start, start.AddTicks(1)).Any();

    /// <summary>How many starts <paramref name="series"/> itself places before <paramref name="at"/>, and the last of them.</summary>
    public (int Count, DateTimeOffset? Last) Before(RecurringSeries series, DateTimeOffset at)
    {
        var (count, last) = (0, (DateTimeOffset?) null);
        foreach (var start in Starts(series, DateTimeOffset.MinValue, at)) (count, last) = (count + 1, start);
        return (count, last);
    }

    private static DateTimeOffset Midnight(DateOnly d) => new(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The starts <paramref name="series"/> itself places in [windowStart, windowEnd), ascending — no extra or
    /// deviating occurrences.</summary>
    public IEnumerable<DateTimeOffset> Starts(RecurringSeries series, DateTimeOffset windowStart, DateTimeOffset windowEnd)
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
