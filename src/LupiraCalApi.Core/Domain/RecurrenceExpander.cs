using Ical.Net;
using Ical.Net.DataTypes;

namespace LupiraCalApi.Domain;

/// <summary>
/// Expands a stored event's recurrence into concrete UTC occurrence starts within a window, using Ical.Net
/// over the canonical <c>source_icalendar</c>. The same expander backs the REST/MCP view and (later) the
/// CalDAV calendar-query serialization, so the agent and the phone never disagree about occurrences.
/// </summary>
public sealed class RecurrenceExpander
{
    public IReadOnlyList<DateTimeOffset> Expand(string sourceIcalendar, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var calendar = Calendar.Load(sourceIcalendar);
        if (calendar is null) return [];

        // GetOccurrences returns a lazy, ascending (possibly infinite) sequence from the given start; the
        // bounded TakeWhile guards infinite RRULEs, and the Where trims any occurrence that began pre-window.
        var start = new CalDateTime(windowStart.UtcDateTime, "UTC");
        return calendar.GetOccurrences(start)
            .TakeWhile(o => o.Period.StartTime.AsUtc < windowEnd.UtcDateTime)
            .Where(o => o.Period.StartTime.AsUtc >= windowStart.UtcDateTime)
            .Select(o => new DateTimeOffset(DateTime.SpecifyKind(o.Period.StartTime.AsUtc, DateTimeKind.Utc), TimeSpan.Zero))
            .ToList();
    }
}
