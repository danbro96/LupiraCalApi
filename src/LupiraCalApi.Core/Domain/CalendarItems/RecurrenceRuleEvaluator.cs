using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using NodaTime;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>Evaluates a recurrence rule for a series anchored at an instant in a zone, yielding occurrence starts as
/// instants in ascending order (possibly without end). Ical.Net is only the rule engine, fed zone-less wall-clock values
/// so the series keeps its local time across offset changes; each start is then mapped into the zone leniently (a start
/// in a forward gap moves later by the gap, one in a backward overlap takes the earlier instant).</summary>
internal static class RecurrenceRuleEvaluator
{
    /// <summary>Starts at or after <paramref name="notBefore"/> are guaranteed; a few earlier ones may precede them.</summary>
    public static IEnumerable<Instant> Evaluate(string rule, Instant anchorAt, DateTimeZone zone, Instant notBefore, bool dateOnly)
    {
        var pattern = new RecurrencePattern(rule);
        var until = Bound(pattern, dateOnly);
        var anchor = anchorAt.InZone(zone).LocalDateTime;
        // Evaluate from a day before notBefore so a zone offset can't hide an occurrence at its edge; a notBefore at or
        // before the anchor (e.g. an all-time search's MinValue) starts at the anchor itself.
        var from = notBefore <= anchorAt ? anchor : notBefore.InZone(zone).LocalDateTime.PlusDays(-1);
        var series = new CalendarEvent { Start = ZoneLess(anchor, dateOnly), RecurrenceRule = pattern };
        foreach (var occurrence in series.GetOccurrences(ZoneLess(from, dateOnly)))
        {
            var start = LocalDateTime.FromDateTime(occurrence.Period.StartTime.Value).InZoneLeniently(zone).ToInstant();
            if (start > until) yield break;
            yield return start;
        }
    }

    /// <summary>Puts the rule's UNTIL where it belongs. A UTC UNTIL is an instant (RFC 5545 requires one when DTSTART
    /// carries a zone), but the zone-less engine would read it on the wall clock — so it's lifted off the pattern and
    /// returned, to bound the mapped instants. A floating UNTIL stays on the pattern and bounds the wall clock. A date
    /// UNTIL on a timed series (invalid per the RFC, but clients send it) covers its whole day.</summary>
    private static Instant? Bound(RecurrencePattern pattern, bool dateOnly)
    {
        if (pattern.Until is not { } until) return null;
        if (until.IsUtc)
        {
            pattern.Until = null;
            return Instant.FromDateTimeUtc(DateTime.SpecifyKind(until.AsUtc, DateTimeKind.Utc));
        }

        if (!dateOnly && !until.HasTime) pattern.Until = new CalDateTime(until.Year, until.Month, until.Day, 23, 59, 59);
        return null;
    }

    private static CalDateTime ZoneLess(LocalDateTime t, bool dateOnly) =>
        dateOnly ? new CalDateTime(t.Year, t.Month, t.Day) : new CalDateTime(t.ToDateTimeUnspecified(), null, true);
}
