using System.Net;
using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>A recurring series with an EXDATE and a RECURRENCE-ID override PUT over DAV must survive GET verbatim
/// and drive time-range expansion (excluded instance gone, overridden instance at its moved time).</summary>
public sealed class DavRecurrenceOverrideTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    // Weekly 09:00 from 2026-07-01; 07-08 excluded (EXDATE); 07-15 moved 09:00 → 14:00 (RECURRENCE-ID override).
    static string RecurringWithOverride(string uid) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//lupira-test//EN\r\n" +
        "BEGIN:VEVENT\r\n" +
        $"UID:{uid}\r\nSUMMARY:Standup\r\nDTSTART:20260701T090000Z\r\nDTEND:20260701T093000Z\r\nRRULE:FREQ=WEEKLY\r\nEXDATE:20260708T090000Z\r\n" +
        "END:VEVENT\r\n" +
        "BEGIN:VEVENT\r\n" +
        $"UID:{uid}\r\nRECURRENCE-ID:20260715T090000Z\r\nSUMMARY:Standup moved\r\nDTSTART:20260715T140000Z\r\nDTEND:20260715T143000Z\r\n" +
        "END:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    [Fact]
    public async Task Exdate_and_override_survive_put_get_and_the_etag_is_stable()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        var icalUid = "rec-override@x";
        var url = $"/dav/u/{uid}/cal/{calId}/{icalUid}.ics";

        var put = await SendDav(dav, "PUT", url, body: RecurringWithOverride(icalUid), contentType: "text/calendar");
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
        var etag = put.Headers.ETag?.Tag;

        var get = await dav.GetAsync(url);
        var body = await get.Content.ReadAsStringAsync();
        Assert.Contains("EXDATE:20260708T090000Z", body);
        Assert.Contains("RECURRENCE-ID:20260715T090000Z", body);
        Assert.Contains("SUMMARY:Standup moved", body);

        // Regeneration is deterministic → the second GET is byte-identical with the same ETag (sync-stable).
        var get2 = await dav.GetAsync(url);
        Assert.Equal(body, await get2.Content.ReadAsStringAsync());
        Assert.Equal(etag, get2.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Time_range_reflects_the_excluded_and_moved_occurrences()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);
        var colUrl = $"/dav/u/{uid}/cal/{calId}/";
        var icalUid = "rec-window@x";
        await SendDav(dav, "PUT", $"{colUrl}{icalUid}.ics", body: RecurringWithOverride(icalUid), contentType: "text/calendar");

        async Task<bool> InWindow(DateTimeOffset s, DateTimeOffset e)
        {
            var doc = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: TimeRangeQueryBody(s, e)));
            return doc.Descendants(D + "href").Any(h => h.Value.Contains(icalUid));
        }

        // Untouched weekly occurrence (07-01 09:00) → present.
        Assert.True(await InWindow(new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 7, 2, 0, 0, 0, TimeSpan.Zero)));
        // 07-08 removed by EXDATE → the whole day is empty.
        Assert.False(await InWindow(new(2026, 7, 8, 0, 0, 0, TimeSpan.Zero), new(2026, 7, 9, 0, 0, 0, TimeSpan.Zero)));
        // 07-15 moved to 14:00 → present only in the afternoon window (the original 09:00 slot is vacated).
        Assert.True(await InWindow(new(2026, 7, 15, 13, 0, 0, TimeSpan.Zero), new(2026, 7, 15, 15, 0, 0, TimeSpan.Zero)));
        Assert.False(await InWindow(new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero), new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero)));
    }
}
