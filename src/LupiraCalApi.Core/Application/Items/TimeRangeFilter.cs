using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Application.Items;

/// <summary>Calendar-query time-range math (half-open: [start, end)), relocated from the retired in-process
/// DAV router — the /dav-backend query endpoint filters server-side so recurrence expansion stays in this domain.</summary>
public sealed class TimeRangeFilter(RecurrenceExpander expander)
{
    public bool Overlaps(CalendarItem i, DateTimeOffset start, DateTimeOffset end)
    {
        if (!string.IsNullOrWhiteSpace(i.RecurrenceRule)) return expander.Expand(i, start, end).Count > 0;
        if (i.IsAllDay && i.StartDate is { } d)
            return CalendarItemService.AllDayInstant(d) < end && CalendarItemService.AllDayInstant(i.EndDate ?? d).AddDays(1) > start;
        if (i.StartsAt is not { } s) return false;
        return s < end && (i.EndsAt ?? s) >= start;
    }
}
