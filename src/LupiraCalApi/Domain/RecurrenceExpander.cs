namespace LupiraCalApi.Domain;

/// <summary>
/// Expands a stored recurring event into concrete occurrences within a window.
///
/// Phase 1: implement with Ical.Net (CalendarEvent.GetOccurrences(start, end) on v5.1+, always with a
/// bounded end to guard infinite RRULEs) over RecurrenceRule + RecurrenceExtraDates +
/// RecurrenceExcludedDates + RecurrenceOverrides. The same expander must back both the REST/MCP view and
/// the CalDAV calendar-query serialization so the agent and the phone never disagree about occurrences.
/// </summary>
public sealed class RecurrenceExpander
{
    public IReadOnlyList<DateTimeOffset> Expand(
        string? recurrenceRule,
        DateTimeOffset seriesStart,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        // TODO(Phase 1): real expansion via Ical.Net. A one-off (no rule) yields just its start if in-window.
        if (string.IsNullOrEmpty(recurrenceRule))
            return seriesStart >= windowStart && seriesStart < windowEnd
                ? new[] { seriesStart }
                : Array.Empty<DateTimeOffset>();

        return Array.Empty<DateTimeOffset>();
    }
}
