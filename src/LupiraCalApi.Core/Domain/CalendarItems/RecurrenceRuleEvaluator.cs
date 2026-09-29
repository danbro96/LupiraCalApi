using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using NodaTime;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>Evaluates a recurrence rule against a wall-clock anchor, yielding wall-clock starts in ascending order
/// (possibly without end). Zone-free on purpose: the caller maps each start into the item's zone. Ical.Net is only the
/// rule engine here, fed zone-less values.</summary>
internal static class RecurrenceRuleEvaluator
{
    public static IEnumerable<LocalDateTime> Evaluate(string rule, LocalDateTime anchor, LocalDateTime from, bool dateOnly)
    {
        var series = new CalendarEvent { Start = ZoneLess(anchor, dateOnly), RecurrenceRule = new RecurrencePattern(rule) };
        return series.GetOccurrences(ZoneLess(from, dateOnly))
            .Select(o => LocalDateTime.FromDateTime(o.Period.StartTime.Value));
    }

    private static CalDateTime ZoneLess(LocalDateTime t, bool dateOnly) =>
        dateOnly ? new CalDateTime(t.Year, t.Month, t.Day) : new CalDateTime(t.ToDateTimeUnspecified(), null, true);
}
