using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class DavQueryTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Time_range_returns_only_items_overlapping_the_window()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);
        var colUrl = $"/dav/u/{uid}/cal/{calId}/";
        var icalUid = "q-1@x";
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        await SendDav(dav, "PUT", $"{colUrl}{icalUid}.ics", body: MinimalIcs(icalUid, "Meeting", start), contentType: "text/calendar");

        var hit = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: TimeRangeQueryBody(start.AddDays(-1), start.AddDays(1))));
        Assert.Contains(WithCalendarData(hit), h => h.Contains(icalUid));

        var miss = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: TimeRangeQueryBody(start.AddYears(1), start.AddYears(1).AddDays(1))));
        Assert.DoesNotContain(WithCalendarData(miss), h => h.Contains(icalUid));
    }

    [Fact]
    public async Task Recurring_item_is_returned_when_an_occurrence_falls_in_the_window()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);
        var colUrl = $"/dav/u/{uid}/cal/{calId}/";
        var icalUid = "q-weekly@x";
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        await SendDav(dav, "PUT", $"{colUrl}{icalUid}.ics", body: MinimalIcs(icalUid, "Weekly", start, rrule: "FREQ=WEEKLY"), contentType: "text/calendar");

        // A window three weeks out — no master DTSTART there, but a weekly occurrence is.
        var doc = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: TimeRangeQueryBody(start.AddDays(20), start.AddDays(27))));
        Assert.Contains(WithCalendarData(doc), h => h.Contains(icalUid));
    }

    [Fact]
    public async Task Multiget_returns_only_the_requested_resource()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);
        var colUrl = $"/dav/u/{uid}/cal/{calId}/";
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        await SendDav(dav, "PUT", $"{colUrl}a@x.ics", body: MinimalIcs("a@x", "A", start), contentType: "text/calendar");
        await SendDav(dav, "PUT", $"{colUrl}b@x.ics", body: MinimalIcs("b@x", "B", start), contentType: "text/calendar");

        var doc = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: MultigetBody($"{colUrl}a@x.ics")));
        var hrefs = WithCalendarData(doc).ToList();
        Assert.Contains(hrefs, h => h.Contains("a@x"));
        Assert.DoesNotContain(hrefs, h => h.Contains("b@x"));
    }

    private static IEnumerable<string> WithCalendarData(XDocument doc) =>
        doc.Descendants(D + "response")
            .Where(r => r.Descendants(C + "calendar-data").Any())
            .Select(r => r.Element(D + "href")?.Value ?? "");
}
