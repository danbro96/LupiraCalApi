using LupiraCalApi.Domain;
using LupiraCalApi.Serialization;
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

    [Fact]
    public void Parser_picks_the_master_vevent_over_a_recurrence_override()
    {
        const string ics =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\n" +
            "BEGIN:VEVENT\r\nUID:e1@x\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T100000Z\r\nSUMMARY:Master\r\nRRULE:FREQ=DAILY\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:e1@x\r\nRECURRENCE-ID:20260702T090000Z\r\nDTSTART:20260702T100000Z\r\nDTEND:20260702T110000Z\r\nSUMMARY:Override\r\nEND:VEVENT\r\n" +
            "END:VCALENDAR\r\n";

        var p = ICalSerializer.ParseICalendar(ics);
        Assert.Equal("Master", p.Title);
    }
}
