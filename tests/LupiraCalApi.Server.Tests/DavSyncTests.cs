using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class DavSyncTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private async Task<(HttpClient dav, string colUrl, string itemUrl, string icalUid)> SetupWithOneItemAsync()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var calId = await CreateCalendarAsync(api);
        var dav = Factory.DavClient(Email);
        var icalUid = "sync-1@x";
        var colUrl = $"/dav/u/{uid}/cal/{calId}/";
        var itemUrl = $"{colUrl}{icalUid}.ics";
        await SendDav(dav, "PUT", itemUrl, body: MinimalIcs(icalUid, "A", new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)), contentType: "text/calendar");
        return (dav, colUrl, itemUrl, icalUid);
    }

    [Fact]
    public async Task Initial_sync_returns_item_and_a_token()
    {
        var (dav, colUrl, _, icalUid) = await SetupWithOneItemAsync();
        var doc = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(null)));

        Assert.Contains(doc.Descendants(D + "href"), h => h.Value.Contains(icalUid));
        var token = RootSyncToken(doc);
        Assert.True(long.TryParse(token, out _));
    }

    [Fact]
    public async Task Delta_sync_reports_the_changed_item_with_a_larger_token()
    {
        var (dav, colUrl, itemUrl, icalUid) = await SetupWithOneItemAsync();
        var initial = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(null)));
        var token1 = long.Parse(RootSyncToken(initial)!);

        // Mutate the item.
        await SendDav(dav, "PUT", itemUrl, body: MinimalIcs(icalUid, "A changed", new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero)), contentType: "text/calendar");

        var delta = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(token1.ToString())));
        var token2 = long.Parse(RootSyncToken(delta)!);

        Assert.True(token2 > token1);
        Assert.Contains(SavedResponses(delta), h => h.Contains(icalUid));
    }

    [Fact]
    public async Task Deleted_item_is_reported_as_a_404_tombstone()
    {
        var (dav, colUrl, itemUrl, icalUid) = await SetupWithOneItemAsync();
        var initial = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(null)));
        var token1 = RootSyncToken(initial)!;

        await SendDav(dav, "DELETE", itemUrl);

        var delta = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(token1)));
        var tombstone = delta.Descendants(D + "response").Any(r =>
            (r.Element(D + "href")?.Value ?? "").Contains(icalUid) &&
            (r.Element(D + "status")?.Value ?? "").Contains("404"));
        Assert.True(tombstone);
    }

    private static IEnumerable<string> SavedResponses(XDocument doc) =>
        doc.Descendants(D + "response")
            .Where(r => r.Descendants(D + "getetag").Any())
            .Select(r => r.Element(D + "href")?.Value ?? "");
}
