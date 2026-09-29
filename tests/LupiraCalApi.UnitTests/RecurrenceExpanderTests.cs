using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Window-bounded expansion: start is inclusive, end exclusive, unbounded rules are guarded; a timed series keeps
/// its wall-clock time in its zone across offset changes; per-occurrence deviations (excluded, extra, overridden).</summary>
public class RecurrenceExpanderTests
{
    private static readonly RecurrenceExpander Expander = new();

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private static CalendarItem Series(DateTimeOffset start, string? rule, string? zone = null) => new()
    {
        Title = "Recurring", StartsAt = start, EndsAt = start.AddHours(1), StartTimezone = zone, RecurrenceRule = rule,
    };

    [Fact]
    public void Weekly_rule_yields_each_occurrence_in_the_window_ascending_and_utc()
    {
        var start = Utc(2026, 7, 1, 9);

        var occ = Expander.Expand(Series(start, "FREQ=WEEKLY"), Utc(2026, 7, 1), Utc(2026, 7, 29));

        Assert.Equal(4, occ.Count);                                   // 07-01, 08, 15, 22 (29 is past the window end)
        Assert.Equal(start, occ[0]);
        Assert.Equal(occ.OrderBy(o => o).ToList(), occ);              // ascending
        Assert.All(occ, o => Assert.Equal(TimeSpan.Zero, o.Offset));  // all UTC
    }

    [Fact]
    public void Window_start_is_inclusive_and_window_end_is_exclusive()
    {
        // Daily at midnight so an occurrence lands exactly on each window edge.
        var item = Series(Utc(2026, 7, 1), "FREQ=DAILY");
        var windowStart = Utc(2026, 7, 2);
        var windowEnd = Utc(2026, 7, 5);

        var occ = Expander.Expand(item, windowStart, windowEnd);

        Assert.Equal(3, occ.Count);                 // 07-02, 03, 04
        Assert.Contains(windowStart, occ);
        Assert.DoesNotContain(windowEnd, occ);
    }

    [Fact]
    public void Unbounded_rule_terminates_and_returns_a_finite_list() =>
        Assert.Equal(10, Expander.Expand(Series(Utc(2026, 7, 1), "FREQ=DAILY"), Utc(2026, 7, 1), Utc(2026, 7, 11)).Count);

    [Fact]
    public void Finite_count_rule_stops_at_its_own_limit_inside_the_window() =>
        Assert.Equal(3, Expander.Expand(Series(Utc(2026, 7, 1), "FREQ=DAILY;COUNT=3"), Utc(2026, 7, 1), Utc(2026, 8, 1)).Count);

    [Fact]
    public void Occurrences_before_the_window_are_trimmed()
    {
        var windowStart = Utc(2026, 7, 1);

        var occ = Expander.Expand(Series(Utc(2026, 6, 1, 9), "FREQ=WEEKLY"), windowStart, Utc(2026, 7, 15));

        Assert.Equal([Utc(2026, 7, 6, 9), Utc(2026, 7, 13, 9)], occ);
    }

    [Fact]
    public void Window_outside_the_item_returns_empty() =>
        Assert.Empty(Expander.Expand(Series(Utc(2026, 7, 1, 9), null), Utc(2026, 1, 1), Utc(2026, 2, 1)));

    [Fact]
    public void MinValue_window_start_expands_from_the_series_start_without_error() =>
        // An all-time search passes MinValue; expansion must anchor at the series start, not year 1.
        Assert.Equal(3, Expander.Expand(Series(Utc(2011, 6, 1, 9), "FREQ=DAILY;COUNT=3"), DateTimeOffset.MinValue, Utc(2011, 7, 1)).Count);

    [Fact]
    public void Zero_width_window_returns_empty()
    {
        var instant = Utc(2026, 7, 1, 9);
        Assert.Empty(Expander.Expand(Series(instant, "FREQ=DAILY"), instant, instant));
    }

    [Fact]
    public void Item_without_a_rule_yields_its_single_start_when_it_falls_in_the_window()
    {
        var start = Utc(2026, 7, 1, 9);
        Assert.Equal(start, Assert.Single(Expander.Expand(Series(start, null), Utc(2026, 7, 1), Utc(2026, 7, 2))));
    }

    [Fact]
    public void Zoned_series_keeps_its_wall_clock_time_across_the_autumn_shift()
    {
        // Every other Sunday 18:00 Stockholm: 16:00Z under summer time, 17:00Z after 2026-10-25.
        var item = Series(Utc(2026, 5, 17, 16), "FREQ=WEEKLY;WKST=MO;INTERVAL=2;BYDAY=SU", "Europe/Stockholm");

        var occ = Expander.Expand(item, Utc(2026, 10, 1), Utc(2026, 12, 1));

        Assert.Equal([Utc(2026, 10, 4, 16), Utc(2026, 10, 18, 16), Utc(2026, 11, 1, 17), Utc(2026, 11, 15, 17), Utc(2026, 11, 29, 17)], occ);
    }

    [Fact]
    public void Zoned_series_keeps_its_wall_clock_time_in_the_southern_hemisphere()
    {
        // Mondays 09:00 Sydney: +11 until 2026-04-05, then +10.
        var item = Series(Utc(2026, 3, 22, 22), "FREQ=WEEKLY", "Australia/Sydney");

        var occ = Expander.Expand(item, Utc(2026, 3, 29), Utc(2026, 4, 10));

        Assert.Equal([Utc(2026, 3, 29, 22), Utc(2026, 4, 5, 23)], occ);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Not/AZone")]
    public void Series_without_a_known_zone_recurs_at_a_fixed_utc_time(string? zone)
    {
        var occ = Expander.Expand(Series(Utc(2026, 5, 17, 16), "FREQ=WEEKLY;INTERVAL=2;BYDAY=SU", zone), Utc(2026, 10, 30), Utc(2026, 11, 2));
        Assert.Equal(Utc(2026, 11, 1, 16), Assert.Single(occ));
    }

    [Fact]
    public void Start_inside_the_spring_gap_moves_later_by_the_gap()
    {
        // 02:30 doesn't exist in Stockholm on 2026-03-29 (02:00 → 03:00): it becomes 03:30 CEST.
        var occ = Expander.Expand(Series(Utc(2026, 3, 27, 1, 30), "FREQ=DAILY", "Europe/Stockholm"), Utc(2026, 3, 28), Utc(2026, 3, 31));
        Assert.Equal([Utc(2026, 3, 28, 1, 30), Utc(2026, 3, 29, 1, 30), Utc(2026, 3, 30, 0, 30)], occ);
    }

    [Fact]
    public void Start_inside_the_autumn_overlap_takes_the_earlier_instant()
    {
        // 02:30 happens twice in Stockholm on 2026-10-25 (03:00 → 02:00): the CEST one.
        var occ = Expander.Expand(Series(Utc(2026, 10, 23, 0, 30), "FREQ=DAILY", "Europe/Stockholm"), Utc(2026, 10, 24), Utc(2026, 10, 27));
        Assert.Equal([Utc(2026, 10, 24, 0, 30), Utc(2026, 10, 25, 0, 30), Utc(2026, 10, 26, 1, 30)], occ);
    }

    [Fact]
    public void All_day_series_recurs_on_dates_at_midnight_utc()
    {
        var item = new CalendarItem { IsAllDay = true, StartDate = new DateOnly(2022, 8, 15), EndDate = new DateOnly(2022, 8, 22), RecurrenceRule = "FREQ=YEARLY;INTERVAL=5" };
        Assert.Equal([Utc(2027, 8, 15), Utc(2032, 8, 15)], Expander.Expand(item, Utc(2026, 1, 1), Utc(2034, 1, 1)));
    }

    [Fact]
    public void Deviations_exclude_add_move_and_cancel_occurrences()
    {
        var item = Series(Utc(2026, 7, 1, 9), "FREQ=WEEKLY");
        item.ExcludedOccurrences = [Utc(2026, 7, 8, 9)];
        item.ExtraOccurrences = [Utc(2026, 7, 10, 12)];
        item.OccurrenceOverrides =
        [
            new OccurrenceOverride(Utc(2026, 7, 15, 9), Utc(2026, 7, 15, 14), null, "Moved", null, null, null),
            new OccurrenceOverride(Utc(2026, 7, 22, 9), null, null, null, null, ItemStatus.Cancelled, null),
            new OccurrenceOverride(Utc(2026, 7, 29, 9), Utc(2026, 7, 28, 9), null, null, null, null, null),   // pulled into the window
        ];

        var occ = Expander.Expand(item, Utc(2026, 7, 1), Utc(2026, 7, 29));

        Assert.Equal([Utc(2026, 7, 1, 9), Utc(2026, 7, 10, 12), Utc(2026, 7, 15, 14), Utc(2026, 7, 28, 9)], occ);
    }
}
