using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Deviations follow their occurrence when the series moves: shifted by the series' wall-clock change, kept only
/// where the moved series still has that occurrence.</summary>
public class SeriesDeviationsTests
{
    private static readonly RecurrenceExpander Expander = new();

    private static DateTimeOffset Utc(int m, int d, int h) => new(2026, m, d, h, 0, 0, TimeSpan.Zero);

    // Sundays 18:00 Stockholm: 16:00Z under summer time, 17:00Z after 2026-10-25.
    private static readonly RecurringSeries Sundays = new(false, Utc(10, 4, 16), null, "Europe/Stockholm", "FREQ=WEEKLY;BYDAY=SU");

    private static OccurrenceOverride Renamed(DateTimeOffset originalStart) => new(originalStart, null, null, "Renamed", null, null, null);

    [Fact]
    public void An_unchanged_series_keeps_them_as_they_are()
    {
        var deviations = new SeriesDeviations([Utc(10, 18, 16)], null);
        Assert.Same(deviations, deviations.Follow(Sundays, Sundays with { }, null, Expander));
    }

    [Fact]
    public void A_new_time_moves_them_on_the_wall_clock_across_an_offset_change()
    {
        var sevenPm = Sundays with { StartsAt = Utc(10, 4, 17) };

        var followed = new SeriesDeviations([Utc(10, 18, 16), Utc(11, 1, 17)], [Renamed(Utc(10, 11, 16))]).Follow(Sundays, sevenPm, null, Expander);

        Assert.Equal([Utc(10, 18, 17), Utc(11, 1, 18)], followed.Excluded!);
        Assert.Equal(Utc(10, 11, 17), Assert.Single(followed.Overrides!).OriginalStart);
    }

    [Fact]
    public void A_new_day_moves_them_to_the_same_week()
    {
        var mondays = Sundays with { StartsAt = Utc(10, 5, 16), RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO" };

        var followed = new SeriesDeviations([Utc(10, 18, 16)], null).Follow(Sundays, mondays, null, Expander);

        Assert.Equal([Utc(10, 19, 16)], followed.Excluded!);
    }

    [Fact]
    public void A_new_zone_keeps_them_on_the_same_local_time()
    {
        var london = Sundays with { StartsAt = Utc(10, 4, 17), StartTimezone = "Europe/London" };

        var followed = new SeriesDeviations([Utc(10, 18, 16), Utc(11, 1, 17)], null).Follow(Sundays, london, null, Expander);

        Assert.Equal([Utc(10, 18, 17), Utc(11, 1, 18)], followed.Excluded!);
    }

    [Fact]
    public void A_rule_that_no_longer_has_the_occurrence_drops_it()
    {
        var firstSundays = Sundays with { RecurrenceRule = "FREQ=MONTHLY;BYDAY=1SU" };

        var followed = new SeriesDeviations([Utc(10, 4, 16), Utc(10, 18, 16)], [Renamed(Utc(10, 11, 16))]).Follow(Sundays, firstSundays, null, Expander);

        Assert.Equal([Utc(10, 4, 16)], followed.Excluded!);
        Assert.Null(followed.Overrides);
    }

    [Fact]
    public void A_series_that_stops_recurring_has_none()
    {
        var once = Sundays with { RecurrenceRule = null };

        var followed = new SeriesDeviations([Utc(10, 4, 16)], [Renamed(Utc(10, 11, 16))]).Follow(Sundays, once, null, Expander);

        Assert.Equal(new SeriesDeviations(null, null), followed);
    }

    [Fact]
    public void An_override_keeps_the_time_it_was_moved_to()
    {
        var moved = new OccurrenceOverride(Utc(10, 11, 16), Utc(10, 12, 16), Utc(10, 12, 18), null, null, ItemStatus.Tentative, null);

        var followed = new SeriesDeviations(null, [moved]).Follow(Sundays, Sundays with { StartsAt = Utc(10, 4, 17) }, null, Expander);

        Assert.Equal(moved with { OriginalStart = Utc(10, 11, 17) }, Assert.Single(followed.Overrides!));
    }

    [Fact]
    public void A_deviation_on_an_extra_occurrence_stays_with_it()
    {
        var extra = Utc(10, 14, 10);

        var followed = new SeriesDeviations([extra], null).Follow(Sundays, Sundays with { StartsAt = Utc(10, 4, 17) }, [extra], Expander);

        Assert.Equal([extra], followed.Excluded!);
    }

    [Fact]
    public void An_all_day_series_moves_them_by_whole_days()
    {
        var saturdays = new RecurringSeries(true, null, new DateOnly(2026, 10, 3), null, "FREQ=WEEKLY");
        var sundays = saturdays with { StartDate = new DateOnly(2026, 10, 4) };

        var followed = new SeriesDeviations([Utc(10, 17, 0)], null).Follow(saturdays, sundays, null, Expander);

        Assert.Equal([Utc(10, 18, 0)], followed.Excluded!);
    }
}
