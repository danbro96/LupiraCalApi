using LupiraCalApi.Core.Domain.Holidays;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class SwedishHolidaysTests
{
    [Fact]
    public void Every_year_has_thirteen_public_holidays_and_seven_eves()
    {
        for (var year = SwedishHolidays.FirstYear; year <= 2100; year++)
        {
            var days = SwedishHolidays.For(year);
            Assert.Equal(13, days.Count(h => h.Kind == HolidayKind.PublicHoliday));
            Assert.Equal(7, days.Count(h => h.Kind == HolidayKind.Eve));
            Assert.All(days, h => Assert.Equal(year, h.Date.Year));
        }
    }

    [Fact]
    public void Years_before_2005_have_none()
    {
        Assert.Empty(SwedishHolidays.For(2004));
    }

    [Theory]
    [InlineData(2005, 3, 27)]
    [InlineData(2008, 3, 23)]
    [InlineData(2011, 4, 24)]
    [InlineData(2019, 4, 21)]
    [InlineData(2024, 3, 31)]
    [InlineData(2026, 4, 5)]
    [InlineData(2038, 4, 25)]
    public void Easter_sunday(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), SwedishHolidays.EasterSunday(year));
    }

    [Fact]
    public void Movable_days_follow_easter()
    {
        var days = SwedishHolidays.For(2026).ToDictionary(h => h.Key, h => h.Date);
        Assert.Equal(new DateOnly(2026, 4, 3), days["langfredagen"]);
        Assert.Equal(new DateOnly(2026, 4, 4), days["paskafton"]);
        Assert.Equal(new DateOnly(2026, 4, 6), days["annandag-pask"]);
        Assert.Equal(new DateOnly(2026, 5, 14), days["kristi-himmelsfardsdag"]);
        Assert.Equal(new DateOnly(2026, 5, 23), days["pingstafton"]);
        Assert.Equal(new DateOnly(2026, 5, 24), days["pingstdagen"]);
    }

    [Fact]
    public void Midsommar_and_alla_helgon_are_saturdays_in_their_ranges()
    {
        for (var year = SwedishHolidays.FirstYear; year <= 2100; year++)
        {
            var days = SwedishHolidays.For(year).ToDictionary(h => h.Key, h => h.Date);
            Assert.Equal(DayOfWeek.Saturday, days["midsommardagen"].DayOfWeek);
            Assert.InRange(days["midsommardagen"], new DateOnly(year, 6, 20), new DateOnly(year, 6, 26));
            Assert.Equal(days["midsommardagen"].AddDays(-1), days["midsommarafton"]);
            Assert.Equal(DayOfWeek.Saturday, days["alla-helgons-dag"].DayOfWeek);
            Assert.InRange(days["alla-helgons-dag"], new DateOnly(year, 10, 31), new DateOnly(year, 11, 6));
        }

        var y2026 = SwedishHolidays.For(2026).ToDictionary(h => h.Key, h => h.Date);
        Assert.Equal(new DateOnly(2026, 6, 20), y2026["midsommardagen"]);
        Assert.Equal(new DateOnly(2026, 10, 31), y2026["alla-helgons-dag"]);
    }

    [Fact]
    public void Days_are_in_date_order()
    {
        var dates = SwedishHolidays.For(2026).Select(h => h.Date).ToList();
        Assert.Equal(dates.Order(), dates);
    }
}
