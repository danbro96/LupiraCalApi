using LupiraCalApi.Core.Application.Holidays;
using LupiraCalApi.Core.Domain.Holidays;
using LupiraCalApi.Core.Dtos.CalendarItems;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class HolidayOccurrenceReaderTests
{
    private static readonly Guid[] Calendars = [Guid.NewGuid()];
    private static readonly HolidayOccurrenceReader Reader = new(new FixedClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));

    private static DateTimeOffset Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_window_keeps_the_days_starting_inside_it()
    {
        var june = Reader.Occurrences(Calendars, null, Utc(2026, 6, 1), Utc(2026, 6, 20));

        Assert.Equal(["Sveriges nationaldag", "Midsommarafton"], june.Select(o => o.Title));
        var eve = june[1];
        Assert.Equal(Utc(2026, 6, 19), eve.Start);
        Assert.Equal(eve.Start, eve.End);
        Assert.True(eve.IsAllDay);
        Assert.Equal(HolidayKind.Eve, eve.Holiday);
        Assert.Equal(OriginKind.Holiday, eve.Origin!.Kind);
        Assert.Equal(eve.Id, eve.Origin.SourceId);
        Assert.Equal(Calendars, eve.CalendarIds);
    }

    [Fact]
    public void A_query_matches_names_case_insensitively()
    {
        var midsommar = Reader.Occurrences(Calendars, "MIDSOMMAR", Utc(2026, 1, 1), Utc(2027, 1, 1));
        Assert.Equal(["Midsommarafton", "Midsommardagen"], midsommar.Select(o => o.Title));
    }

    [Fact]
    public void Nothing_before_2005_and_nothing_without_a_calendar()
    {
        Assert.Empty(Reader.Occurrences(Calendars, null, Utc(2000, 1, 1), Utc(2005, 1, 1)));
        Assert.Single(Reader.Occurrences(Calendars, null, DateTimeOffset.MinValue, Utc(2005, 1, 2)));
        Assert.Empty(Reader.Occurrences([], null, Utc(2026, 1, 1), Utc(2027, 1, 1)));
    }

    [Fact]
    public void A_holiday_keeps_its_id_across_years()
    {
        var christmases = Reader.Occurrences(Calendars, "juldagen", Utc(2025, 1, 1), Utc(2028, 1, 1));
        Assert.Equal(3, christmases.Count);
        Assert.Single(christmases.Select(o => o.Id).Distinct());
        Assert.Equal(3, christmases.Select(o => o.Etag).Distinct().Count());
    }

    [Fact]
    public void The_snapshot_covers_last_year_through_two_years_ahead()
    {
        var snapshot = Reader.Snapshot(Calendars);

        Assert.Equal(20, snapshot.Count);
        var jul = snapshot.Single(h => h.Name == "Juldagen");
        Assert.Equal(HolidayKind.PublicHoliday, jul.Kind);
        Assert.Equal([new(2025, 12, 25), new(2026, 12, 25), new(2027, 12, 25), new(2028, 12, 25)], jul.Dates);
        Assert.Empty(Reader.Snapshot([]));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
