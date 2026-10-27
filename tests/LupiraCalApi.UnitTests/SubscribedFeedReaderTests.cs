using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Serialization;
using Xunit;

namespace LupiraCalApi.UnitTests;

/// <summary>Published feeds as the big providers export them, read into subscribed events.</summary>
public class SubscribedFeedReaderTests
{
    private static readonly Guid SubscriptionId = Guid.Parse("6f1c1b9e-3c55-4a8e-9a43-0e7f4c1d2b10");

    private static readonly string GoogleExport = Feed(
        "BEGIN:VCALENDAR",
        "PRODID:-//Google Inc//Google Calendar 70.9054//EN",
        "VERSION:2.0",
        "CALSCALE:GREGORIAN",
        "METHOD:PUBLISH",
        "X-WR-CALNAME:Klubben",
        "X-WR-TIMEZONE:Europe/Stockholm",
        "BEGIN:VTIMEZONE",
        "TZID:Europe/Stockholm",
        "X-LIC-LOCATION:Europe/Stockholm",
        "BEGIN:DAYLIGHT",
        "TZOFFSETFROM:+0100",
        "TZOFFSETTO:+0200",
        "TZNAME:CEST",
        "DTSTART:19700329T020000",
        "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU",
        "END:DAYLIGHT",
        "BEGIN:STANDARD",
        "TZOFFSETFROM:+0200",
        "TZOFFSETTO:+0100",
        "TZNAME:CET",
        "DTSTART:19701025T030000",
        "RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU",
        "END:STANDARD",
        "END:VTIMEZONE",
        "BEGIN:VEVENT",
        "DTSTART;TZID=Europe/Stockholm:20260105T180000",
        "DTEND;TZID=Europe/Stockholm:20260105T193000",
        "RRULE:FREQ=WEEKLY;BYDAY=MO",
        "EXDATE;TZID=Europe/Stockholm:20260112T180000",
        "DTSTAMP:20260101T000000Z",
        "UID:training-1@google.com",
        "DESCRIPTION:Ta med skor",
        "LOCATION:Sporthallen",
        "SEQUENCE:0",
        "STATUS:CONFIRMED",
        "SUMMARY:Träning",
        "TRANSP:OPAQUE",
        "END:VEVENT",
        "BEGIN:VEVENT",
        "DTSTART;TZID=Europe/Stockholm:20260119T190000",
        "DTEND;TZID=Europe/Stockholm:20260119T203000",
        "DTSTAMP:20260101T000000Z",
        "UID:training-1@google.com",
        "RECURRENCE-ID;TZID=Europe/Stockholm:20260119T180000",
        "SUMMARY:Träning (flyttad)",
        "STATUS:CONFIRMED",
        "END:VEVENT",
        "BEGIN:VEVENT",
        "DTSTART:20260301T100000",
        "DTEND:20260301T110000",
        "DTSTAMP:20260101T000000Z",
        "UID:floating-1@google.com",
        "SUMMARY:Årsmöte",
        "END:VEVENT",
        "END:VCALENDAR");

    private static readonly string OutlookExport = Feed(
        "BEGIN:VCALENDAR",
        "METHOD:PUBLISH",
        "PRODID:Microsoft Exchange Server 2010",
        "VERSION:2.0",
        "X-WR-CALNAME:Calendar",
        "BEGIN:VTIMEZONE",
        "TZID:W. Europe Standard Time",
        "BEGIN:STANDARD",
        "DTSTART:16010101T030000",
        "TZOFFSETFROM:+0200",
        "TZOFFSETTO:+0100",
        "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=10",
        "END:STANDARD",
        "BEGIN:DAYLIGHT",
        "DTSTART:16010101T020000",
        "TZOFFSETFROM:+0100",
        "TZOFFSETTO:+0200",
        "RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=3",
        "END:DAYLIGHT",
        "END:VTIMEZONE",
        "BEGIN:VEVENT",
        "UID:040000008200E00074C5B7101A82E00800000000",
        "SUMMARY:Möte",
        "DTSTART;TZID=W. Europe Standard Time:20260610T090000",
        "DTEND;TZID=W. Europe Standard Time:20260610T100000",
        "CLASS:PUBLIC",
        "PRIORITY:5",
        "DTSTAMP:20260601T000000Z",
        "TRANSP:OPAQUE",
        "STATUS:CONFIRMED",
        "SEQUENCE:0",
        "LOCATION:Teams",
        "END:VEVENT",
        "END:VCALENDAR");

    [Fact]
    public void Google_export_keeps_the_series_its_exclusion_and_its_moved_occurrence()
    {
        var events = SubscribedFeedReader.Read(SubscriptionId, GoogleExport, "UTC");

        var training = Assert.Single(events, e => e.SourceKey == "training-1@google.com");
        var s = training.Series;
        Assert.Equal("Träning", s.Title);
        Assert.Equal("Ta med skor", s.Description);
        Assert.Equal("Sporthallen", s.LocationLabel);
        Assert.Equal(ItemStatus.Confirmed, s.Status);
        Assert.Equal("Europe/Stockholm", s.StartTimezone);
        Assert.Equal(Utc(2026, 1, 5, 17), s.StartsAt);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO", s.RecurrenceRule);
        Assert.Equal([Utc(2026, 1, 12, 17)], s.ExcludedOccurrences!);
        var moved = Assert.Single(s.OccurrenceOverrides!);
        Assert.Equal(Utc(2026, 1, 19, 17), moved.OriginalStart);
        Assert.Equal(Utc(2026, 1, 19, 18), moved.StartsAt);
        Assert.Equal("Träning (flyttad)", moved.Title);
        Assert.Equal(Utc(2026, 1, 5, 17), training.FirstStart);
        Assert.Null(training.LastEnd);

        var slots = OccurrenceSlots.Of(s.Schedule(), new RecurrenceExpander(), Utc(2026, 1, 1), Utc(2026, 2, 1), Utc(2026, 2, 1)).ToList();
        Assert.Equal([Utc(2026, 1, 5, 17), Utc(2026, 1, 19, 18), Utc(2026, 1, 26, 17)], slots.Select(x => x.Start));
    }

    [Fact]
    public void Floating_times_read_in_the_feeds_own_zone_before_the_calendars()
    {
        var floating = Assert.Single(SubscribedFeedReader.Read(SubscriptionId, GoogleExport, "America/New_York"), e => e.SourceKey == "floating-1@google.com");

        Assert.Equal(Utc(2026, 3, 1, 9), floating.Series.StartsAt);
        Assert.Equal(Utc(2026, 3, 1, 10), floating.LastEnd);
    }

    [Fact]
    public void Floating_times_without_a_feed_zone_read_in_the_calendars_zone()
    {
        var feed = Single("BEGIN:VEVENT\r\nUID:f\r\nDTSTART:20260301T100000\r\nDTEND:20260301T110000\r\nSUMMARY:Lokal\r\nEND:VEVENT");

        Assert.Equal(Utc(2026, 3, 1, 15), Assert.Single(SubscribedFeedReader.Read(SubscriptionId, feed, "America/New_York")).Series.StartsAt);
    }

    [Fact]
    public void Outlook_export_maps_its_windows_zone()
    {
        var meeting = Assert.Single(SubscribedFeedReader.Read(SubscriptionId, OutlookExport, "UTC"));

        Assert.Equal("Möte", meeting.Series.Title);
        Assert.Equal("Teams", meeting.Series.LocationLabel);
        Assert.Equal(Utc(2026, 6, 10, 7), meeting.Series.StartsAt);
        Assert.Equal(Utc(2026, 6, 10, 8), meeting.Series.EndsAt);
        Assert.Equal("Europe/Berlin", meeting.Series.StartTimezone);
        Assert.Equal(Utc(2026, 6, 10, 8), meeting.LastEnd);
    }

    [Fact]
    public void All_day_multi_day_event_keeps_its_inclusive_last_day_and_covers_through_it()
    {
        var feed = Single("BEGIN:VEVENT\r\nUID:camp\r\nDTSTART;VALUE=DATE:20260710\r\nDTEND;VALUE=DATE:20260713\r\nSUMMARY:Läger\r\nEND:VEVENT");

        var camp = Assert.Single(SubscribedFeedReader.Read(SubscriptionId, feed, "Europe/Stockholm"));

        Assert.True(camp.Series.IsAllDay);
        Assert.Equal(new DateOnly(2026, 7, 10), camp.Series.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 12), camp.Series.EndDate);
        Assert.Equal(Utc(2026, 7, 10), camp.FirstStart);
        Assert.Equal(Utc(2026, 7, 13), camp.LastEnd);
    }

    [Fact]
    public void A_cancelled_event_is_left_out()
    {
        var feed = Single(
            "BEGIN:VEVENT\r\nUID:off\r\nDTSTART:20260301T100000Z\r\nSUMMARY:Inställt\r\nSTATUS:CANCELLED\r\nEND:VEVENT\r\n"
            + "BEGIN:VEVENT\r\nUID:on\r\nDTSTART:20260302T100000Z\r\nSUMMARY:Av\r\nEND:VEVENT");

        Assert.Equal(["on"], SubscribedFeedReader.Read(SubscriptionId, feed, "UTC").Select(e => e.SourceKey));
    }

    [Fact]
    public void An_event_without_a_uid_gets_a_stable_key_from_its_start_and_title()
    {
        var feed = Single("BEGIN:VEVENT\r\nDTSTART:20260301T100000Z\r\nSUMMARY:Utan id\r\nEND:VEVENT");

        var first = Assert.Single(SubscribedFeedReader.Read(SubscriptionId, feed, "UTC"));
        var again = Assert.Single(SubscribedFeedReader.Read(SubscriptionId, feed, "UTC"));

        Assert.Equal(first.SourceKey, again.SourceKey);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.ContentHash, again.ContentHash);
        Assert.NotEqual(Assert.Single(SubscribedFeedReader.Read(Guid.NewGuid(), feed, "UTC")).Id, first.Id);
    }

    [Fact]
    public void A_changed_event_changes_its_hash()
    {
        var before = Assert.Single(SubscribedFeedReader.Read(SubscriptionId, Single("BEGIN:VEVENT\r\nUID:a\r\nDTSTART:20260301T100000Z\r\nSUMMARY:Före\r\nEND:VEVENT"), "UTC"));
        var after = Assert.Single(SubscribedFeedReader.Read(SubscriptionId, Single("BEGIN:VEVENT\r\nUID:a\r\nDTSTART:20260301T100000Z\r\nSUMMARY:Efter\r\nEND:VEVENT"), "UTC"));

        Assert.Equal(before.Id, after.Id);
        Assert.NotEqual(before.ContentHash, after.ContentHash);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Sign in</body></html>")]
    [InlineData("")]
    [InlineData("{\"events\":[]}")]
    public void Anything_but_a_calendar_is_refused(string body) =>
        Assert.Throws<FormatException>(() => SubscribedFeedReader.Read(SubscriptionId, body, "UTC"));

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private static string Feed(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static string Single(string vevent) => Feed("BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//test//EN", vevent, "END:VCALENDAR");
}
