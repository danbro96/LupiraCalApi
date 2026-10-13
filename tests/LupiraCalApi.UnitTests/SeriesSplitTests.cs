using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>A series cut at one of its occurrences: the earlier part's rule ends just before it, the following part starts
/// at it, deviations go with their occurrence, and together the parts have exactly the series' occurrences. A moved bounded
/// series keeps its last occurrence.</summary>
public class SeriesSplitTests
{
    private static readonly RecurrenceExpander Expander = new();

    private static DateTimeOffset Utc(int m, int d, int h = 0, int min = 0) => new(2026, m, d, h, min, 0, TimeSpan.Zero);

    private static CalendarItem Timed(DateTimeOffset start, string rule, string zone = "UTC") => new()
    {
        StartsAt = start, EndsAt = start.AddHours(2), StartTimezone = zone, RecurrenceRule = rule,
    };

    private static CalendarItem ItemOf(SeriesPart part) => new()
    {
        IsAllDay = part.Series.IsAllDay, StartsAt = part.Series.StartsAt, EndsAt = part.EndsAt, StartDate = part.Series.StartDate,
        EndDate = part.EndDate, StartTimezone = part.Series.StartTimezone, RecurrenceRule = part.Series.RecurrenceRule,
        ExcludedOccurrences = part.Excluded, ExtraOccurrences = part.Extra, OccurrenceOverrides = part.Overrides,
    };

    private static SeriesSplit SplitWithSameOccurrences(CalendarItem item, DateTimeOffset at)
    {
        var split = SeriesSplit.At(item, at, Expander)!;
        var (from, to) = (Utc(1, 1), new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var earlier = Expander.Occurrences(ItemOf(split.Earlier), from, to);
        var following = Expander.Occurrences(ItemOf(split.Following), from, to);
        Assert.All(earlier, o => Assert.True(o.OriginalStart < at));
        Assert.All(following, o => Assert.True(o.OriginalStart >= at));
        Assert.Equal(Expander.Occurrences(item, from, to), [.. earlier.Concat(following).OrderBy(o => o.Start).ThenBy(o => o.OriginalStart)]);
        return split;
    }

    [Fact]
    public void A_counted_series_divides_its_count()
    {
        var split = SplitWithSameOccurrences(Timed(Utc(7, 6, 9), "FREQ=WEEKLY;BYDAY=MO;COUNT=5"), Utc(7, 20, 9));

        Assert.Equal("FREQ=WEEKLY;BYDAY=MO;COUNT=2", split.Earlier.Series.RecurrenceRule);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO;COUNT=3", split.Following.Series.RecurrenceRule);
        Assert.Equal(Utc(7, 20, 9), split.Following.Series.StartsAt);
        Assert.Equal(Utc(7, 20, 11), split.Following.EndsAt);
    }

    [Fact]
    public void An_open_series_ends_at_the_last_earlier_start_and_the_rest_continues_open()
    {
        var split = SplitWithSameOccurrences(Timed(Utc(7, 6, 9), "FREQ=WEEKLY"), Utc(7, 27, 9));

        Assert.Equal("FREQ=WEEKLY;UNTIL=20260720T090000Z", split.Earlier.Series.RecurrenceRule);
        Assert.Equal("FREQ=WEEKLY", split.Following.Series.RecurrenceRule);
    }

    [Fact]
    public void A_bounded_series_keeps_its_bound_on_the_following_part()
    {
        var split = SplitWithSameOccurrences(Timed(Utc(7, 6, 9), "FREQ=DAILY;UNTIL=20260715T090000Z"), Utc(7, 10, 9));

        Assert.Equal("FREQ=DAILY;UNTIL=20260709T090000Z", split.Earlier.Series.RecurrenceRule);
        Assert.Equal("FREQ=DAILY;UNTIL=20260715T090000Z", split.Following.Series.RecurrenceRule);
    }

    [Fact]
    public void An_all_day_series_ends_on_a_date_and_continues_at_its_length()
    {
        var item = new CalendarItem { IsAllDay = true, StartDate = new DateOnly(2026, 7, 4), EndDate = new DateOnly(2026, 7, 5), RecurrenceRule = "FREQ=WEEKLY" };

        var split = SplitWithSameOccurrences(item, Utc(7, 18));

        Assert.Equal("FREQ=WEEKLY;UNTIL=20260711", split.Earlier.Series.RecurrenceRule);
        Assert.Equal(new DateOnly(2026, 7, 18), split.Following.Series.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 19), split.Following.EndDate);
    }

    [Fact]
    public void A_zoned_series_split_across_the_autumn_shift_keeps_its_wall_clock_time()
    {
        // Sundays 18:00 Stockholm: 16:00Z under summer time, 17:00Z after 2026-10-25.
        var split = SplitWithSameOccurrences(Timed(Utc(10, 4, 16), "FREQ=WEEKLY;BYDAY=SU", "Europe/Stockholm"), Utc(11, 1, 17));

        Assert.Equal("FREQ=WEEKLY;BYDAY=SU;UNTIL=20261025T170000Z", split.Earlier.Series.RecurrenceRule);
        Assert.Equal(Utc(11, 1, 17), split.Following.Series.StartsAt);
        Assert.Equal("Europe/Stockholm", split.Following.Series.StartTimezone);
    }

    [Fact]
    public void A_sub_second_start_splits_on_its_whole_second_occurrences_and_a_last_start_rounds_up()
    {
        var split = SplitWithSameOccurrences(Timed(Utc(7, 6, 9).AddMilliseconds(500), "FREQ=DAILY"), Utc(7, 9, 9));

        Assert.Equal("FREQ=DAILY;UNTIL=20260708T090000Z", split.Earlier.Series.RecurrenceRule);
        Assert.Equal("FREQ=DAILY;UNTIL=20260708T090001Z", RecurrenceRuleBounds.WithLastStart("FREQ=DAILY", Utc(7, 8, 9).AddMilliseconds(500), dateOnly: false));
    }

    [Fact]
    public void Deviations_go_with_the_part_their_occurrence_falls_in()
    {
        var item = Timed(Utc(7, 6, 9), "FREQ=WEEKLY");
        item.ExcludedOccurrences = [Utc(7, 13, 9), Utc(8, 3, 9)];
        item.ExtraOccurrences = [Utc(7, 15, 12), Utc(7, 29, 12)];
        var early = new OccurrenceOverride(Utc(7, 6, 9), null, null, "First", null, null, null);
        var late = new OccurrenceOverride(Utc(7, 27, 9), Utc(7, 19, 9), null, "Pulled earlier", null, null, null);
        item.OccurrenceOverrides = [early, late];

        var split = SplitWithSameOccurrences(item, Utc(7, 20, 9));

        Assert.Equal([Utc(7, 13, 9)], split.Earlier.Excluded!);
        Assert.Equal([Utc(7, 15, 12)], split.Earlier.Extra!);
        Assert.Equal([early], split.Earlier.Overrides!);
        Assert.Equal([Utc(8, 3, 9)], split.Following.Excluded!);
        Assert.Equal([Utc(7, 29, 12)], split.Following.Extra!);
        Assert.Equal([late], split.Following.Overrides!);
    }

    [Fact]
    public void Only_a_later_occurrence_of_the_rule_splits_it()
    {
        var item = Timed(Utc(7, 6, 9), "FREQ=WEEKLY");
        item.ExtraOccurrences = [Utc(7, 15, 12)];

        Assert.Null(SeriesSplit.At(item, Utc(7, 6, 9), Expander));
        Assert.Null(SeriesSplit.At(item, Utc(7, 15, 12), Expander));
        Assert.Null(SeriesSplit.At(item, Utc(7, 14, 9), Expander));
    }

    [Theory]
    [InlineData("FREQ=DAILY;UNTIL=20260710T090000Z", "FREQ=DAILY;UNTIL=20260710T100000Z")]
    [InlineData("FREQ=DAILY;UNTIL=20260710T110000", "FREQ=DAILY;UNTIL=20260710T120000")]
    [InlineData("FREQ=DAILY;UNTIL=20260710", "FREQ=DAILY;UNTIL=20260710")]
    [InlineData("FREQ=DAILY;COUNT=5", "FREQ=DAILY;COUNT=5")]
    public void A_moved_bounded_series_moves_its_last_start_along(string rule, string moved)
    {
        var before = new RecurringSeries(false, Utc(7, 6, 9), null, "Europe/Stockholm", rule);
        var later = before with { StartsAt = Utc(7, 6, 10) };

        var after = later.KeepingBoundOf(before);

        Assert.Equal(moved, after.RecurrenceRule);
        Assert.Equal(
            Expander.Starts(before, Utc(1, 1), Utc(12, 1)).Count(),
            Expander.Starts(after, Utc(1, 1), Utc(12, 1)).Count());
    }

    [Fact]
    public void A_moved_all_day_series_moves_its_last_date_by_whole_days()
    {
        var before = new RecurringSeries(true, null, new DateOnly(2026, 7, 4), null, "FREQ=WEEKLY;UNTIL=20260725");

        Assert.Equal("FREQ=WEEKLY;UNTIL=20260726", (before with { StartDate = new DateOnly(2026, 7, 5) }).KeepingBoundOf(before).RecurrenceRule);
    }

    [Fact]
    public void A_new_rule_is_taken_as_written()
    {
        var before = new RecurringSeries(false, Utc(7, 6, 9), null, "UTC", "FREQ=DAILY;UNTIL=20260710T090000Z");
        var after = new RecurringSeries(false, Utc(7, 6, 10), null, "UTC", "FREQ=DAILY;UNTIL=20260712T100000Z");

        Assert.Same(after, after.KeepingBoundOf(before));
    }

    [Fact]
    public void A_continued_participation_id_is_derived_from_the_continuation_and_the_original()
    {
        var (continuation, participation) = (Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(SeriesSplit.ContinuedParticipationId(continuation, participation), SeriesSplit.ContinuedParticipationId(continuation, participation));
        Assert.NotEqual(SeriesSplit.ContinuedParticipationId(continuation, participation), SeriesSplit.ContinuedParticipationId(Guid.NewGuid(), participation));
    }
}
