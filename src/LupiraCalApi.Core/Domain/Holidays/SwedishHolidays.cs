namespace LupiraCalApi.Core.Domain.Holidays;

/// <summary>Swedish public holidays and eves, computed per year under the rules in force since 2005.</summary>
public static class SwedishHolidays
{
    /// <summary>Nationaldagen became a public holiday and Annandag pingst stopped being one in 2005.</summary>
    public const int FirstYear = 2005;

    public static IReadOnlyList<Holiday> For(int year)
    {
        if (year < FirstYear || year > DateOnly.MaxValue.Year) return [];

        var easter = EasterSunday(year);
        var midsommardagen = SaturdayFrom(new DateOnly(year, 6, 20));
        Holiday[] days =
        [
            new("nyarsdagen", "Nyårsdagen", new DateOnly(year, 1, 1), HolidayKind.PublicHoliday),
            new("trettondagsafton", "Trettondagsafton", new DateOnly(year, 1, 5), HolidayKind.Eve),
            new("trettondedag-jul", "Trettondedag jul", new DateOnly(year, 1, 6), HolidayKind.PublicHoliday),
            new("langfredagen", "Långfredagen", easter.AddDays(-2), HolidayKind.PublicHoliday),
            new("paskafton", "Påskafton", easter.AddDays(-1), HolidayKind.Eve),
            new("paskdagen", "Påskdagen", easter, HolidayKind.PublicHoliday),
            new("annandag-pask", "Annandag påsk", easter.AddDays(1), HolidayKind.PublicHoliday),
            new("valborgsmassoafton", "Valborgsmässoafton", new DateOnly(year, 4, 30), HolidayKind.Eve),
            new("forsta-maj", "Första maj", new DateOnly(year, 5, 1), HolidayKind.PublicHoliday),
            new("kristi-himmelsfardsdag", "Kristi himmelsfärdsdag", easter.AddDays(39), HolidayKind.PublicHoliday),
            new("pingstafton", "Pingstafton", easter.AddDays(48), HolidayKind.Eve),
            new("pingstdagen", "Pingstdagen", easter.AddDays(49), HolidayKind.PublicHoliday),
            new("nationaldagen", "Sveriges nationaldag", new DateOnly(year, 6, 6), HolidayKind.PublicHoliday),
            new("midsommarafton", "Midsommarafton", midsommardagen.AddDays(-1), HolidayKind.Eve),
            new("midsommardagen", "Midsommardagen", midsommardagen, HolidayKind.PublicHoliday),
            new("alla-helgons-dag", "Alla helgons dag", SaturdayFrom(new DateOnly(year, 10, 31)), HolidayKind.PublicHoliday),
            new("julafton", "Julafton", new DateOnly(year, 12, 24), HolidayKind.Eve),
            new("juldagen", "Juldagen", new DateOnly(year, 12, 25), HolidayKind.PublicHoliday),
            new("annandag-jul", "Annandag jul", new DateOnly(year, 12, 26), HolidayKind.PublicHoliday),
            new("nyarsafton", "Nyårsafton", new DateOnly(year, 12, 31), HolidayKind.Eve),
        ];
        return [.. days.OrderBy(h => h.Date)];
    }

    // Anonymous Gregorian algorithm (Meeus/Jones/Butcher).
    internal static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static DateOnly SaturdayFrom(DateOnly d) => d.AddDays(((int)DayOfWeek.Saturday - (int)d.DayOfWeek + 7) % 7);
}
