using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class CardDavSyncTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private async Task<(HttpClient dav, string colUrl, string itemUrl, string vcardUid)> SetupWithOneContactAsync()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var dav = Factory.DavClient(Email);
        var vcardUid = "sync-c1@x";
        var colUrl = $"/dav/u/{uid}/card/{abId}/";
        var itemUrl = $"{colUrl}{vcardUid}.vcf";
        await SendDav(dav, "PUT", itemUrl, body: MinimalVcf(vcardUid, "Jane"), contentType: "text/vcard");
        return (dav, colUrl, itemUrl, vcardUid);
    }

    [Fact]
    public async Task Initial_sync_returns_contact_and_a_token()
    {
        var (dav, colUrl, _, vcardUid) = await SetupWithOneContactAsync();
        var doc = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(null)));

        Assert.Contains(doc.Descendants(D + "href"), h => h.Value.Contains(vcardUid));
        Assert.True(long.TryParse(RootSyncToken(doc), out _));
    }

    [Fact]
    public async Task Delta_sync_reports_the_changed_contact_with_a_larger_token()
    {
        var (dav, colUrl, itemUrl, vcardUid) = await SetupWithOneContactAsync();
        var initial = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(null)));
        var token1 = long.Parse(RootSyncToken(initial)!);

        await SendDav(dav, "PUT", itemUrl, body: MinimalVcf(vcardUid, "Jane Roe"), contentType: "text/vcard");

        var delta = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(token1.ToString())));
        var token2 = long.Parse(RootSyncToken(delta)!);
        Assert.True(token2 > token1);
        Assert.Contains(delta.Descendants(D + "response").Where(r => r.Descendants(D + "getetag").Any()),
            r => (r.Element(D + "href")?.Value ?? "").Contains(vcardUid));
    }

    [Fact]
    public async Task Deleted_contact_is_reported_as_a_404_tombstone()
    {
        var (dav, colUrl, itemUrl, vcardUid) = await SetupWithOneContactAsync();
        var initial = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(null)));
        var token1 = RootSyncToken(initial)!;

        await SendDav(dav, "DELETE", itemUrl);

        var delta = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: SyncCollectionBody(token1)));
        var tombstone = delta.Descendants(D + "response").Any(r =>
            (r.Element(D + "href")?.Value ?? "").Contains(vcardUid) &&
            (r.Element(D + "status")?.Value ?? "").Contains("404"));
        Assert.True(tombstone);
    }
}
