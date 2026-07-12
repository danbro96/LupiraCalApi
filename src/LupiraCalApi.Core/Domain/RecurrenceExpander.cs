using Ical.Net.DataTypes;
using LupiraCalApi.Serialization;
using IcalCalendar = Ical.Net.Calendar;

namespace LupiraCalApi.Domain;

/// <summary>
/// Expands an event's recurrence into concrete UTC occurrence starts within a window, using Ical.Net. Generation is from
/// the item's canonical structured fields (RRULE/DTSTART) — location/title don't affect occurrences. The same expander
/// backs the REST/MCP view and the CalDAV calendar-query, so the agent and the phone never disagree about occurrences.
/// </summary>
public sealed class RecurrenceExpander
{
    /// <summary>Expand from an item's canonical fields (no stored blob needed). Location is irrelevant to recurrence, so omitted.</summary>
    public IReadOnlyList<DateTimeOffset> Expand(CalendarItem item, DateTimeOffset windowStart, DateTimeOffset windowEnd) =>
        Expand(ICalSerializer.From(item, null), windowStart, windowEnd);

    public IReadOnlyList<DateTimeOffset> Expand(string sourceIcalendar, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var calendar = IcalCalendar.Load(sourceIcalendar);
        if (calendar is null) return [];

        // Ical.Net can't evaluate rules from an unrepresentable period start (e.g. DateTimeOffset.MinValue from an
        // all-time text search); no occurrence precedes the earliest DTSTART, so clamp the period start to it.
        var floor = calendar.Events.Min(e => e.DtStart?.AsUtc);
        if (floor is null) return [];
        var startUtc = windowStart.UtcDateTime > floor.Value ? windowStart.UtcDateTime : floor.Value;

        // GetOccurrences returns a lazy, ascending (possibly infinite) sequence from the given start; the
        // bounded TakeWhile guards infinite RRULEs, and the Where trims any occurrence that began pre-window.
        var start = new CalDateTime(startUtc, "UTC");
        return calendar.GetOccurrences(start)
            .TakeWhile(o => o.Period.StartTime.AsUtc < windowEnd.UtcDateTime)
            .Where(o => o.Period.StartTime.AsUtc >= windowStart.UtcDateTime)
            .Select(o => new DateTimeOffset(DateTime.SpecifyKind(o.Period.StartTime.AsUtc, DateTimeKind.Utc), TimeSpan.Zero))
            .ToList();
    }
}
