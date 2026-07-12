using LupiraCalApi.Domain;
using LupiraCalApi.Serialization;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Window-bounded RRULE expansion: start is inclusive, end exclusive, infinite rules are guarded.
/// Inputs are authored via <see cref="ICalSerializer.ToICalendar"/> so the recurrence rule rides on a real blob.</summary>
public class RecurrenceExpanderTests
{
    static readonly RecurrenceExpander Expander = new();

    static string Ics(DateTimeOffset start, string? rrule, TimeSpan? duration = null) =>
        ICalSerializer.ToICalendar("uid@x", "Recurring", null, null, null, false,
            start, start + (duration ?? TimeSpan.FromHours(1)), null, null, rrule);

    [Fact]
    public void Weekly_rule_yields_each_occurrence_in_the_window_ascending_and_utc()
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var ics = Ics(start, "FREQ=WEEKLY");

        var occ = Expander.Expand(ics, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(4, occ.Count);                                   // 07-01, 08, 15, 22 (29 is past the window end)
        Assert.Equal(start, occ[0]);
        Assert.Equal(occ.OrderBy(o => o).ToList(), occ);              // ascending
        Assert.All(occ, o => Assert.Equal(TimeSpan.Zero, o.Offset));  // all UTC
    }

    [Fact]
    public void Window_start_is_inclusive_and_window_end_is_exclusive()
    {
        // Daily at midnight so an occurrence lands exactly on each window edge.
        var ics = Ics(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), "FREQ=DAILY");
        var windowStart = new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero);

        var occ = Expander.Expand(ics, windowStart, windowEnd);

        Assert.Equal(3, occ.Count);                 // 07-02, 03, 04
        Assert.Contains(windowStart, occ);          // start edge included
        Assert.DoesNotContain(windowEnd, occ);      // end edge excluded
    }

    [Fact]
    public void Infinite_rule_terminates_and_returns_a_finite_list()
    {
        var ics = Ics(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), "FREQ=DAILY");  // no COUNT/UNTIL

        var occ = Expander.Expand(ics, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(10, occ.Count);                // 07-01..07-10
    }

    [Fact]
    public void Finite_count_rule_stops_at_its_own_limit_inside_the_window()
    {
        var ics = Ics(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), "FREQ=DAILY;COUNT=3");

        var occ = Expander.Expand(ics, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, occ.Count);
    }

    [Fact]
    public void Occurrences_before_the_window_are_trimmed()
    {
        // Series anchored in June; only the July occurrences inside the window survive.
        var ics = Ics(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), "FREQ=WEEKLY");
        var windowStart = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var occ = Expander.Expand(ics, windowStart, new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, occ.Count);                                 // 07-06, 07-13
        Assert.All(occ, o => Assert.True(o >= windowStart));
    }

    [Fact]
    public void Window_outside_the_series_returns_empty()
    {
        var ics = Ics(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), null);  // single, non-recurring

        var occ = Expander.Expand(ics, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(occ);
    }

    [Fact]
    public void MinValue_window_start_expands_from_dtstart_without_error()
    {
        // Query-only search passes an all-time window start; expansion must anchor at DTSTART, not year 1.
        var ics = Ics(new DateTimeOffset(2011, 6, 1, 9, 0, 0, TimeSpan.Zero), "FREQ=DAILY;COUNT=3");

        var occ = Expander.Expand(ics, DateTimeOffset.MinValue, new DateTimeOffset(2011, 7, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, occ.Count);
    }

    [Fact]
    public void Zero_width_window_returns_empty()
    {
        var instant = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var ics = Ics(instant, "FREQ=DAILY");

        Assert.Empty(Expander.Expand(ics, instant, instant));
    }

    [Fact]
    public void Non_recurring_event_yields_a_single_occurrence_when_it_falls_in_the_window()
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var ics = Ics(start, null);

        var occ = Expander.Expand(ics, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(start, Assert.Single(occ));
    }

    // A master VEVENT (weekly) plus an EXDATE (drops 07-08) and a RECURRENCE-ID override (moves 07-15 09:00 → 14:00).
    // Confirms Ical.Net resolves exceptions and per-instance overrides when the whole set is in one calendar.
    const string MasterWithOverrideAndException = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//test//EN
        BEGIN:VEVENT
        UID:uid@x
        DTSTAMP:20000101T000000Z
        DTSTART:20260701T090000Z
        DTEND:20260701T100000Z
        SUMMARY:Recurring
        RRULE:FREQ=WEEKLY
        EXDATE:20260708T090000Z
        END:VEVENT
        BEGIN:VEVENT
        UID:uid@x
        DTSTAMP:20000101T000000Z
        RECURRENCE-ID:20260715T090000Z
        DTSTART:20260715T140000Z
        DTEND:20260715T150000Z
        SUMMARY:Recurring moved
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Exdate_and_recurrence_id_override_are_honored()
    {
        var occ = Expander.Expand(MasterWithOverrideAndException,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero));

        Assert.DoesNotContain(new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.Zero), occ);   // EXDATE dropped it
        Assert.DoesNotContain(new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero), occ);  // original slot gone
        Assert.Contains(new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero), occ);       // moved to 14:00
        Assert.Equal(3, occ.Count);                                                          // 07-01, 07-15(moved), 07-22
    }
}
