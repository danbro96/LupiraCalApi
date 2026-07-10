using LupiraCalApi.Domain;

namespace LupiraCalApi.Application;

/// <summary>Calendar-query time-range math (half-open: [start, end)), relocated from the retired in-process
/// DAV router — the /dav-backend query endpoint filters server-side so recurrence expansion stays in this domain.</summary>
public sealed class TimeRangeFilter(RecurrenceExpander expander)
{
    public bool Overlaps(CalendarItem i, DateTimeOffset start, DateTimeOffset end)
    {
        if (!string.IsNullOrWhiteSpace(i.RecurrenceRule)) return expander.Expand(i, start, end).Count > 0;
        DateTimeOffset? s = i.IsAllDay && i.StartDate is { } d
            ? new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero) : i.StartsAt;
        if (s is null) return false;
        var en = i.EndsAt ?? (i.IsAllDay && i.EndDate is { } ed
            ? new DateTimeOffset(ed.Year, ed.Month, ed.Day, 0, 0, 0, TimeSpan.Zero) : s.Value);
        return s.Value < end && en >= start;
    }
}
