using System.Net;
using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class DavReadWriteTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Propfind_root_advertises_current_user_principal()
    {
        var dav = Factory.DavClient(Email);
        var resp = await SendDav(dav, "PROPFIND", "/dav/", depth: "0");
        Assert.Equal(207, (int)resp.StatusCode);
        var doc = await ReadXml(resp);
        Assert.NotEmpty(doc.Descendants(D + "current-user-principal"));
    }

    [Fact]
    public async Task Propfind_principal_advertises_home_sets()
    {
        var uid = await GetMyIdAsync(Factory.ApiClient(Email));
        var dav = Factory.DavClient(Email);
        var doc = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/", depth: "0"));
        Assert.NotEmpty(doc.Descendants(C + "calendar-home-set"));
        Assert.NotEmpty(doc.Descendants(XNamespace.Get("urn:ietf:params:xml:ns:carddav") + "addressbook-home-set"));
    }

    [Fact]
    public async Task Propfind_calendar_home_lists_the_calendar()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        var doc = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/cal/", depth: "1"));
        var hrefs = doc.Descendants(D + "href").Select(h => h.Value);
        Assert.Contains(hrefs, h => h.Contains($"/cal/{calId}/"));
    }

    [Fact]
    public async Task Put_then_get_round_trips_semantically_with_a_stable_etag()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        var icalUid = "evt-1@x";
        var ics = MinimalIcs(icalUid, "Lunch", new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var url = $"/dav/u/{uid}/cal/{calId}/{icalUid}.ics";

        var put = await SendDav(dav, "PUT", url, body: ics, contentType: "text/calendar");
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
        var etag = put.Headers.ETag?.Tag;
        Assert.False(string.IsNullOrWhiteSpace(etag));

        // GET regenerates the ICS from canonical fields (not the verbatim blob): semantic round-trip + ETag from the PUT.
        var get = await dav.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadAsStringAsync();
        Assert.Contains($"UID:{icalUid}", body);
        Assert.Contains("SUMMARY:Lunch", body);
        Assert.Equal(etag, get.Headers.ETag?.Tag);

        // Generation is deterministic → a second GET is byte-identical with the same ETag (stable for sync).
        var get2 = await dav.GetAsync(url);
        Assert.Equal(body, await get2.Content.ReadAsStringAsync());
        Assert.Equal(etag, get2.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Preconditions_are_enforced()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        var icalUid = "evt-2@x";
        var url = $"/dav/u/{uid}/cal/{calId}/{icalUid}.ics";
        var ics = MinimalIcs(icalUid, "Lunch", new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

        var created = await SendDav(dav, "PUT", url, body: ics, contentType: "text/calendar");
        var etag = created.Headers.ETag!.Tag;

        // If-None-Match:* against an existing resource must fail.
        var dup = await SendDav(dav, "PUT", url, body: ics, ifNoneMatch: "*", contentType: "text/calendar");
        Assert.Equal(HttpStatusCode.PreconditionFailed, dup.StatusCode);

        // Wrong If-Match must fail.
        var wrong = await SendDav(dav, "PUT", url, body: ics, ifMatch: "\"deadbeef\"", contentType: "text/calendar");
        Assert.Equal(HttpStatusCode.PreconditionFailed, wrong.StatusCode);

        // Correct If-Match updates (204).
        var ics2 = MinimalIcs(icalUid, "Lunch moved", new DateTimeOffset(2026, 7, 1, 13, 0, 0, TimeSpan.Zero));
        var ok = await SendDav(dav, "PUT", url, body: ics2, ifMatch: etag, contentType: "text/calendar");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Delete_then_put_resurrects_the_same_uid()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);

        var icalUid = "evt-3@x";
        var url = $"/dav/u/{uid}/cal/{calId}/{icalUid}.ics";
        var ics = MinimalIcs(icalUid, "Lunch", new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(HttpStatusCode.Created, (await SendDav(dav, "PUT", url, body: ics, contentType: "text/calendar")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendDav(dav, "DELETE", url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dav.GetAsync(url)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await SendDav(dav, "PUT", url, body: ics, contentType: "text/calendar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dav.GetAsync(url)).StatusCode);
    }
}
