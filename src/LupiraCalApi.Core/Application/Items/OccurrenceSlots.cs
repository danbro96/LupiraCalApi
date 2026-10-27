using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Application.Items;

/// <summary>The occurrences of a series that overlap a search window — shared by stored items and subscribed events, so
/// both expand alike.</summary>
internal static class OccurrenceSlots
{
    public static IEnumerable<OccurrenceSlot> Of(
        CalendarItem i, RecurrenceExpander expander, DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset expansionEnd)
    {
        // All-day items carry their span in StartDate/EndDate, not StartsAt/EndsAt; derive the end at the
        // inclusive last day's 00:00Z (same convention as TimeRangeFilter) so multi-day all-day occurrences
        // report an End instead of null.
        TimeSpan? duration =
            i.StartsAt is { } s && i.EndsAt is { } en ? en - s
            : i.IsAllDay && i.StartDate is { } sd && i.EndDate is { } ed ? CalendarItemService.AllDayInstant(ed) - CalendarItemService.AllDayInstant(sd)
            : null;
        // The window matches occurrences overlapping it, not only those starting in it: a photo taken mid-trip
        // belongs to a trip that began that morning. An all-day item covers through its inclusive last day.
        var coverage =
            i.StartsAt is { } cs && i.EndsAt is { } ce && ce > cs ? ce - cs
            : i.IsAllDay && i.StartDate is { } csd ? CalendarItemService.AllDayInstant(i.EndDate ?? csd).AddDays(1) - CalendarItemService.AllDayInstant(csd)
            : TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(i.RecurrenceRule))
        {
            foreach (var occ in expander.Occurrences(i, Lookback(windowStart, coverage), expansionEnd))
            {
                var change = occ.Change;
                var coveredUntil = change?.EndsAt is { } changedEnd ? (i.IsAllDay ? changedEnd.AddDays(1) : changedEnd) : occ.Start + coverage;
                if (!Overlaps(occ.Start, coveredUntil, windowStart, windowEnd)) continue;
                yield return new OccurrenceSlot(occ.Start, change?.EndsAt ?? (duration is { } d ? occ.Start + d : null), occ.OriginalStart, change);
            }
        }
        else if (CalendarItemService.OccurrenceStart(i) is { } start && Overlaps(start, start + coverage, windowStart, windowEnd))
        {
            yield return new OccurrenceSlot(start, duration is { } d ? start + d : i.EndsAt, null, null);
        }
    }

    // Half-open [start, end); a zero-length occurrence still matches at its start.
    private static bool Overlaps(DateTimeOffset start, DateTimeOffset end, DateTimeOffset windowStart, DateTimeOffset windowEnd) =>
        start < windowEnd && (start >= windowStart || end > windowStart);

    private static DateTimeOffset Lookback(DateTimeOffset windowStart, TimeSpan coverage) =>
        windowStart - DateTimeOffset.MinValue > coverage ? windowStart - coverage : DateTimeOffset.MinValue;
}
