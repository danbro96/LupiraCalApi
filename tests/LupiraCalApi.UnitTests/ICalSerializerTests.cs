using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Serialization;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>iCalendar author + parse: happy-path round-trips, all-day vs timed, STATUS mapping, error paths
/// (malformed / no-VEVENT), and master selection when overrides (RECURRENCE-ID) share the blob.</summary>
public class ICalSerializerTests
{
    [Fact]
    public void ToICalendar_then_parse_preserves_core_fields()
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var ics = ICalSerializer.ToICalendar("uid@x", "Lunch", "desc", "Office", ItemStatus.Confirmed, false, start, start.AddHours(1), null, null, "FREQ=WEEKLY");
        var p = ICalSerializer.ParseICalendar(ics);

        Assert.Equal("Lunch", p.Title);
        Assert.Equal("Office", p.Location);
        Assert.Equal("FREQ=WEEKLY", p.RecurrenceRule);
        Assert.Equal(start, p.StartsAt);
        Assert.Equal(start.AddHours(1), p.EndsAt);
        Assert.False(p.IsAllDay);
    }

    [Fact]
    public void All_day_round_trips_as_a_date()
    {
        var ics = ICalSerializer.ToICalendar("uid@x", "Holiday", null, null, null, true, null, null, new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25), null);
        var p = ICalSerializer.ParseICalendar(ics);
        Assert.True(p.IsAllDay);
        Assert.Equal(new DateOnly(2026, 12, 24), p.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 25), p.EndDate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void One_day_all_day_item_ends_exclusively_the_next_day(bool withEndDate)
    {
        var day = new DateOnly(2026, 12, 24);
        var ics = ICalSerializer.ToICalendar("uid@x", "Eve", null, null, null, true, null, null, day, withEndDate ? day : null, null);

        Assert.Contains("DTSTART;VALUE=DATE:20261224", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261225", ics);
    }

    [Fact]
    public void Multi_day_all_day_item_round_trips_its_last_day()
    {
        var ics = ICalSerializer.ToICalendar("uid@x", "Trip", null, null, null, true, null, null, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 13), null);
        var p = ICalSerializer.ParseICalendar(ics);

        Assert.Contains("DTEND;VALUE=DATE:20260714", ics);
        Assert.Equal(new DateOnly(2026, 7, 10), p.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 13), p.EndDate);
    }

    [Theory]
    [InlineData("DTEND;VALUE=DATE:20260714\r\n", 13)]
    [InlineData("DTEND;VALUE=DATE:20260710\r\n", 10)]   // zero-length: clamped to the start
    [InlineData("", null)]
    public void All_day_end_parses_to_the_last_day(string dtend, int? lastDay)
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\nUID:e1@x\r\nDTSTART;VALUE=DATE:20260710\r\n" +
            dtend + "END:VEVENT\r\nEND:VCALENDAR\r\n";

        Assert.Equal(lastDay is { } d ? new DateOnly(2026, 7, d) : null, ICalSerializer.ParseICalendar(ics).EndDate);
    }

    [Fact]
    public void All_day_override_keeps_the_series_length()
    {
        var moved = new[] { new OccurrenceOverride(Utc(2026, 8, 3), Utc(2026, 8, 4), null, null, null, null, null) };
        var ics = ICalSerializer.ToICalendar("week@x", "Camp", null, null, null, true, null, null,
            new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 29), "FREQ=WEEKLY;COUNT=3", null, null, moved);

        var change = ics[ics.LastIndexOf("BEGIN:VEVENT", StringComparison.Ordinal)..];
        Assert.Contains("RECURRENCE-ID;VALUE=DATE:20260803", change);
        Assert.Contains("DTSTART;VALUE=DATE:20260804", change);
        Assert.Contains("DTEND;VALUE=DATE:20260807", change);
        Assert.Equal(moved, ICalSerializer.ParseICalendar(ics).OccurrenceOverrides);
    }

    [Fact]
    public void All_day_regeneration_is_byte_stable_with_a_fixed_product_id()
    {
        string Regen() => ICalSerializer.ToICalendar("trip@x", "Trip", null, null, null, true, null, null,
            new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 13), "FREQ=YEARLY", [Utc(2027, 7, 10)]);

        var first = Regen();
        Assert.Equal(first, Regen());
        Assert.Contains("PRODID:-//lupira.com//LupiraCal//EN", first);
        var p = ICalSerializer.ParseICalendar(first);
        Assert.Equal(first, ICalSerializer.ToICalendar("trip@x", p.Title, null, null, null, p.IsAllDay, null, null,
            p.StartDate, p.EndDate, p.RecurrenceRule, p.ExcludedOccurrences));
    }

    [Fact]
    public void Missing_optional_fields_round_trip_cleanly()
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var ics = ICalSerializer.ToICalendar("uid@x", "Bare", null, null, null, false, start, null, null, null, null);
        var p = ICalSerializer.ParseICalendar(ics);

        Assert.Equal("Bare", p.Title);
        Assert.Null(p.Description);
        Assert.Null(p.Location);
        Assert.Null(p.RecurrenceRule);
        Assert.Null(p.EndsAt);
        Assert.False(p.IsAllDay);
    }

    [Theory]
    [InlineData(ItemStatus.Confirmed, "STATUS:CONFIRMED")]
    [InlineData(ItemStatus.Cancelled, "STATUS:CANCELLED")]
    [InlineData(ItemStatus.Tentative, "STATUS:TENTATIVE")]
    public void Status_is_serialized_to_the_ical_keyword(ItemStatus status, string expected)
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var ics = ICalSerializer.ToICalendar("uid@x", "S", null, null, status, false, start, start.AddHours(1), null, null, null);
        Assert.Contains(expected, ics);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not iCalendar")]
    public void Malformed_payload_throws_format_exception(string raw) =>
        Assert.Throws<FormatException>(() => ICalSerializer.ParseICalendar(raw));

    [Fact]
    public void Calendar_without_a_vevent_throws_format_exception()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nEND:VCALENDAR\r\n";
        Assert.Throws<FormatException>(() => ICalSerializer.ParseICalendar(ics));
    }

    private const string MasterWithExdateAndOverride =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n" +
        "BEGIN:VEVENT\r\nUID:e1@x\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T100000Z\r\nSUMMARY:Master\r\nRRULE:FREQ=DAILY\r\nEXDATE:20260703T090000Z\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:e1@x\r\nRECURRENCE-ID:20260702T090000Z\r\nDTSTART:20260702T100000Z\r\nDTEND:20260702T110000Z\r\nSUMMARY:Override\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    [Fact]
    public void Parser_picks_the_master_vevent_over_a_recurrence_override()
    {
        var p = ICalSerializer.ParseICalendar(MasterWithExdateAndOverride);
        Assert.Equal("Master", p.Title);
    }

    [Fact]
    public void Exception_dates_and_override_vevents_parse_into_structured_deviations()
    {
        var p = ICalSerializer.ParseICalendar(MasterWithExdateAndOverride);

        Assert.Equal([new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero)], p.ExcludedOccurrences!);
        var o = Assert.Single(p.OccurrenceOverrides!);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero), o.OriginalStart);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero), o.StartsAt);
        Assert.Null(o.EndsAt);                // same length as the series
        Assert.Equal("Override", o.Title);
    }

    [Fact]
    public void Zoned_multi_value_exception_dates_and_extra_dates_parse_to_instants()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\nUID:e1@x\r\n" +
            "DTSTART;TZID=Europe/Stockholm:20260517T180000\r\nDTEND;TZID=Europe/Stockholm:20260517T210000\r\nRRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=SU\r\n" +
            "EXDATE;TZID=Europe/Stockholm:20260614T180000,20260628T180000\r\nEXDATE:20260712T160000Z\r\nRDATE:20260801T160000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var p = ICalSerializer.ParseICalendar(ics);

        Assert.Equal("Europe/Stockholm", p.StartTimezone);
        Assert.Equal([Utc(2026, 6, 14, 16), Utc(2026, 6, 28, 16), Utc(2026, 7, 12, 16)], p.ExcludedOccurrences!);
        Assert.Equal([Utc(2026, 8, 1, 16)], p.ExtraOccurrences!);
    }

    [Fact]
    public void Regenerated_ics_carries_the_deviations_and_is_byte_stable()
    {
        var p = ICalSerializer.ParseICalendar(MasterWithExdateAndOverride);
        string Regen() => ICalSerializer.ToICalendar("e1@x", p.Title, p.Description, null, null, false,
            p.StartsAt, p.EndsAt, p.StartDate, p.EndDate, p.RecurrenceRule, p.ExcludedOccurrences, p.ExtraOccurrences, p.OccurrenceOverrides);

        var first = Regen();
        Assert.Equal(first, Regen());                              // deterministic → stable ETag across reads
        Assert.Contains("EXDATE:20260703T090000Z", first);
        Assert.Contains("RECURRENCE-ID:20260702T090000Z", first);
        Assert.Contains("SUMMARY:Override", first);

        var reparsed = ICalSerializer.ParseICalendar(first);        // survives a full round-trip
        Assert.Equal(p.ExcludedOccurrences, reparsed.ExcludedOccurrences);
        Assert.Equal(p.OccurrenceOverrides, reparsed.OccurrenceOverrides);
    }

    [Fact]
    public void All_day_series_deviations_round_trip_as_dates()
    {
        var excluded = new[] { Utc(2027, 8, 15) };
        var moved = new[] { new OccurrenceOverride(Utc(2032, 8, 15), Utc(2032, 8, 16), null, null, null, null, null) };

        var ics = ICalSerializer.ToICalendar("tbe@x", "Vaccinera", null, null, null, true, null, null,
            new DateOnly(2022, 8, 15), new DateOnly(2022, 8, 22), "FREQ=YEARLY;INTERVAL=5", excluded, null, moved);
        var p = ICalSerializer.ParseICalendar(ics);

        Assert.Contains("EXDATE;VALUE=DATE:20270815", ics);
        Assert.Equal(excluded, p.ExcludedOccurrences);
        Assert.Equal(moved, p.OccurrenceOverrides);
    }

    [Fact]
    public void Cancelled_override_parses_as_a_cancelled_occurrence()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n" +
            "BEGIN:VEVENT\r\nUID:e1@x\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T100000Z\r\nSUMMARY:M\r\nRRULE:FREQ=DAILY\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:e1@x\r\nRECURRENCE-ID:20260702T090000Z\r\nDTSTART:20260702T090000Z\r\nDTEND:20260702T100000Z\r\nSUMMARY:M\r\nSTATUS:CANCELLED\r\nEND:VEVENT\r\n" +
            "END:VCALENDAR\r\n";

        var o = Assert.Single(ICalSerializer.ParseICalendar(ics).OccurrenceOverrides!);

        Assert.Equal(new OccurrenceOverride(Utc(2026, 7, 2, 9), null, null, null, null, ItemStatus.Cancelled, null), o);
    }

    [Fact]
    public void Recurring_timed_series_is_written_in_its_zone()
    {
        var start = Utc(2026, 5, 17, 16);   // 18:00 CEST
        var ics = ICalSerializer.ToICalendar("theo@x", "Middag", null, null, null, false, start, start.AddHours(3), null, null,
            "FREQ=WEEKLY;INTERVAL=2;BYDAY=SU", [Utc(2026, 11, 15, 17)], null, null, "Europe/Stockholm");

        Assert.Contains("BEGIN:VTIMEZONE", ics);
        Assert.Contains("DTSTART;TZID=Europe/Stockholm:20260517T180000", ics);
        Assert.Contains("EXDATE;TZID=Europe/Stockholm:20261115T180000", ics);
        Assert.Equal(ics, ICalSerializer.ToICalendar("theo@x", "Middag", null, null, null, false, start, start.AddHours(3), null, null,
            "FREQ=WEEKLY;INTERVAL=2;BYDAY=SU", [Utc(2026, 11, 15, 17)], null, null, "Europe/Stockholm"));   // byte-stable

        var p = ICalSerializer.ParseICalendar(ics);
        Assert.Equal(start, p.StartsAt);
        Assert.Equal("Europe/Stockholm", p.StartTimezone);
        Assert.Equal([Utc(2026, 11, 15, 17)], p.ExcludedOccurrences!);
    }

    [Theory]
    [InlineData(null, "Europe/Stockholm")]    // one-off: an instant needs no zone
    [InlineData("FREQ=DAILY", "Not/AZone")]   // unknown zone
    [InlineData("FREQ=DAILY", null)]
    public void Other_timed_items_stay_in_utc(string? rule, string? zone)
    {
        var start = Utc(2026, 5, 17, 16);
        var ics = ICalSerializer.ToICalendar("x@x", "T", null, null, null, false, start, start.AddHours(1), null, null, rule, startTimezone: zone);

        Assert.DoesNotContain("VTIMEZONE", ics);
        Assert.Contains("DTSTART:20260517T160000Z", ics);
    }

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, TimeSpan.Zero);
}
