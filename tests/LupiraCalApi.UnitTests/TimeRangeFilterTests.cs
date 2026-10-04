using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.CalendarItems;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class TimeRangeFilterTests
{
    private static readonly TimeRangeFilter Filter = new(new RecurrenceExpander());

    private static readonly CalendarItem Trip = new() { IsAllDay = true, StartDate = new DateOnly(2026, 7, 10), EndDate = new DateOnly(2026, 7, 13) };

    [Fact]
    public void All_day_item_covers_its_whole_last_day() =>
        Assert.True(Filter.Overlaps(Trip, Utc(2026, 7, 13, 12), Utc(2026, 7, 14)));

    [Fact]
    public void All_day_item_ends_before_the_next_day() =>
        Assert.False(Filter.Overlaps(Trip, Utc(2026, 7, 14), Utc(2026, 7, 15)));

    [Fact]
    public void One_day_item_without_an_end_covers_its_day() =>
        Assert.True(Filter.Overlaps(new CalendarItem { IsAllDay = true, StartDate = new DateOnly(2026, 7, 10) }, Utc(2026, 7, 10, 18), Utc(2026, 7, 11)));

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, TimeSpan.Zero);
}
