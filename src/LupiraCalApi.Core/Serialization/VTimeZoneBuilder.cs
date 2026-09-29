using System.Globalization;
using System.Text;
using NodaTime;

namespace LupiraCalApi.Core.Serialization;

/// <summary>
/// Authors the VTIMEZONE a DAV client needs to expand a zoned series, from NodaTime's tzdb (Ical.Net's own generator
/// emits wrong offsets for some anchors). Each offset change must recur yearly on one month / nth-or-last weekday / wall
/// time across the whole horizon — true of current tzdb rules; otherwise null and the caller renders UTC times instead.
/// </summary>
internal static class VTimeZoneBuilder
{
    private const int HorizonYears = 25;

    private static readonly string[] Weekdays = ["", "MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    public static string? Build(DateTimeZone zone, int fromYear)
    {
        var start = Instant.FromUtc(fromYear - 1, 1, 1, 0, 0);
        var end = Instant.FromUtc(fromYear + HorizonYears, 1, 1, 0, 0);
        var intervals = zone.GetZoneIntervals(start, end).ToList();
        var sb = new StringBuilder("BEGIN:VTIMEZONE\r\nTZID:").Append(zone.Id).Append("\r\n");

        if (intervals.Count == 1)
        {
            var fixedOffset = intervals[0].WallOffset;
            Observance(sb, "STANDARD", new LocalDateTime(1970, 1, 1, 0, 0), null, intervals[0].Name, fixedOffset, fixedOffset);
            return sb.Append("END:VTIMEZONE\r\n").ToString();
        }

        var transitions = intervals.Zip(intervals.Skip(1), (prev, next) =>
            (Wall: next.Start.WithOffset(prev.WallOffset).LocalDateTime, From: prev.WallOffset, To: next.WallOffset, next.Name)).ToList();
        var years = HorizonYears + 1;
        // Clocks forward = DAYLIGHT, back = STANDARD, by offset direction (tzdb's negative-DST zones stay correct).
        foreach (var kind in transitions.GroupBy(t => t.To > t.From).OrderBy(g => g.Key))
        {
            var list = kind.ToList();
            var first = list[0];
            if (list.Count != years || list.Select(t => t.Wall.Year).Distinct().Count() != years) return null;
            if (list.Any(t => t.From != first.From || t.To != first.To || t.Name != first.Name
                || t.Wall.Month != first.Wall.Month || t.Wall.DayOfWeek != first.Wall.DayOfWeek || t.Wall.TimeOfDay != first.Wall.TimeOfDay))
                return null;
            var last = list.All(t => t.Wall.Day + 7 > CalendarSystem.Iso.GetDaysInMonth(t.Wall.Year, t.Wall.Month));
            var nth = (first.Wall.Day - 1) / 7 + 1;
            if (!last && list.Any(t => (t.Wall.Day - 1) / 7 + 1 != nth)) return null;

            var byDay = (last ? "-1" : nth.ToString(CultureInfo.InvariantCulture)) + Weekdays[(int) first.Wall.DayOfWeek];
            Observance(sb, kind.Key ? "DAYLIGHT" : "STANDARD", first.Wall,
                $"FREQ=YEARLY;BYMONTH={first.Wall.Month};BYDAY={byDay}", first.Name, first.From, first.To);
        }

        return transitions.GroupBy(t => t.To > t.From).Count() == 2 ? sb.Append("END:VTIMEZONE\r\n").ToString() : null;
    }

    private static void Observance(StringBuilder sb, string kind, LocalDateTime onset, string? rule, string name, Offset from, Offset to)
    {
        sb.Append("BEGIN:").Append(kind).Append("\r\n")
            .Append("DTSTART:").Append(onset.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)).Append("\r\n");
        if (rule is not null) sb.Append("RRULE:").Append(rule).Append("\r\n");
        sb.Append("TZNAME:").Append(name).Append("\r\n")
            .Append("TZOFFSETFROM:").Append(Format(from)).Append("\r\n")
            .Append("TZOFFSETTO:").Append(Format(to)).Append("\r\n")
            .Append("END:").Append(kind).Append("\r\n");
    }

    private static string Format(Offset o)
    {
        var total = Math.Abs(o.Seconds);
        var text = $"{(o.Seconds < 0 ? '-' : '+')}{total / 3600:00}{total / 60 % 60:00}";
        return total % 60 == 0 ? text : text + $"{total % 60:00}";
    }
}
