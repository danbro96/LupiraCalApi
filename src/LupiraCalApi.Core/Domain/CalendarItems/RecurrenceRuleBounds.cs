using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>Reads and rewrites the end of a recurrence rule — its occurrence count or its last start — in place, leaving the
/// rule's other parts as written.</summary>
internal static class RecurrenceRuleBounds
{
    private static readonly LocalDatePattern DatePattern = LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd");
    private static readonly LocalDateTimePattern WallClockPattern = LocalDateTimePattern.CreateWithInvariantCulture("uuuuMMdd'T'HHmmss");

    public static int? Count(string rule) =>
        Part(rule, "COUNT") is { } value && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    public static string WithCount(string rule, int count) => With(rule, "COUNT", count.ToString(CultureInfo.InvariantCulture));

    /// <summary>The rule ending with the occurrence at <paramref name="lastStart"/>: its date for an all-day series, else the
    /// instant rounded up to the whole second the rule can carry.</summary>
    public static string WithLastStart(string rule, DateTimeOffset lastStart, bool dateOnly) =>
        With(rule, "UNTIL", dateOnly ? DatePattern.Format(LocalDate.FromDateTime(lastStart.UtcDateTime)) : Utc(RoundedUp(lastStart)));

    /// <summary>The rule with its last start moved along with the series, so a moved bounded series keeps its occurrences.</summary>
    public static string Shifted(string rule, SeriesShift shift)
    {
        if (Part(rule, "UNTIL") is not { } until) return rule;
        if (until.EndsWith('Z') && WallClockPattern.Parse(until[..^1]) is { Success: true } utc)
            return With(rule, "UNTIL", Utc(shift.Apply(utc.Value.InUtc().ToDateTimeOffset())));
        if (WallClockPattern.Parse(until) is { Success: true } wallClock)
            return With(rule, "UNTIL", WallClockPattern.Format(shift.Apply(wallClock.Value)));
        if (DatePattern.Parse(until) is { Success: true } date)
            return With(rule, "UNTIL", DatePattern.Format(shift.Apply(date.Value.AtMidnight()).Date));
        return rule;
    }

    private static string Utc(DateTimeOffset at) => WallClockPattern.Format(LocalDateTime.FromDateTime(at.UtcDateTime)) + "Z";

    private static DateTimeOffset RoundedUp(DateTimeOffset at) =>
        at.UtcTicks % TimeSpan.TicksPerSecond is var excess and > 0 ? at.AddTicks(TimeSpan.TicksPerSecond - excess) : at;

    private static string? Part(string rule, string name) =>
        rule.Split(';').Select(p => p.Split('=', 2)).FirstOrDefault(kv => kv.Length == 2 && kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))?[1].Trim();

    // A rule ends by a count or a last start, never both: setting one drops the other.
    private static string With(string rule, string name, string value)
    {
        var other = name == "COUNT" ? "UNTIL" : "COUNT";
        var parts = rule.Split(';').Where(p => !Named(p, other)).ToList();
        var at = parts.FindIndex(p => Named(p, name));
        if (at >= 0) parts[at] = $"{name}={value}";
        else parts.Add($"{name}={value}");
        return string.Join(';', parts);
    }

    private static bool Named(string part, string name) => part.Split('=', 2)[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase);
}
