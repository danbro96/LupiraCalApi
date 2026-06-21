using System.Net;
using System.Xml.Linq;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

public sealed class CardDavReadWriteTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Propfind_addressbook_home_lists_the_book()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var dav = Factory.DavClient(Email);

        var doc = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/card/", depth: "1"));
        Assert.Contains(doc.Descendants(D + "href"), h => h.Value.Contains($"/card/{abId}/"));
    }

    [Fact]
    public async Task Put_then_get_returns_byte_identical_vcard_and_matching_etag()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var dav = Factory.DavClient(Email);

        var vcardUid = "c-1@x";
        var vcf = MinimalVcf(vcardUid, "Jane Doe", "jane@x.test");
        var url = $"/dav/u/{uid}/card/{abId}/{vcardUid}.vcf";

        var put = await SendDav(dav, "PUT", url, body: vcf, contentType: "text/vcard");
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
        var etag = put.Headers.ETag?.Tag;
        Assert.False(string.IsNullOrWhiteSpace(etag));

        var get = await dav.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(vcf, await get.Content.ReadAsStringAsync());
        Assert.Equal(etag, get.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Propfind_addressbook_lists_its_contacts()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var dav = Factory.DavClient(Email);

        var vcardUid = "c-list@x";
        await SendDav(dav, "PUT", $"/dav/u/{uid}/card/{abId}/{vcardUid}.vcf", body: MinimalVcf(vcardUid, "Jane"), contentType: "text/vcard");

        var doc = await ReadXml(await SendDav(dav, "PROPFIND", $"/dav/u/{uid}/card/{abId}/", depth: "1"));
        Assert.Contains(doc.Descendants(D + "href"), h => h.Value.Contains(vcardUid));
    }

    [Fact]
    public async Task Preconditions_are_enforced()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var dav = Factory.DavClient(Email);

        var vcardUid = "c-2@x";
        var url = $"/dav/u/{uid}/card/{abId}/{vcardUid}.vcf";
        var vcf = MinimalVcf(vcardUid, "Jane");

        var created = await SendDav(dav, "PUT", url, body: vcf, contentType: "text/vcard");
        var etag = created.Headers.ETag!.Tag;

        var dup = await SendDav(dav, "PUT", url, body: vcf, ifNoneMatch: "*", contentType: "text/vcard");
        Assert.Equal(HttpStatusCode.PreconditionFailed, dup.StatusCode);

        var wrong = await SendDav(dav, "PUT", url, body: vcf, ifMatch: "\"deadbeef\"", contentType: "text/vcard");
        Assert.Equal(HttpStatusCode.PreconditionFailed, wrong.StatusCode);

        var ok = await SendDav(dav, "PUT", url, body: MinimalVcf(vcardUid, "Jane Roe"), ifMatch: etag, contentType: "text/vcard");
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_contact()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var dav = Factory.DavClient(Email);

        var vcardUid = "c-3@x";
        var url = $"/dav/u/{uid}/card/{abId}/{vcardUid}.vcf";
        await SendDav(dav, "PUT", url, body: MinimalVcf(vcardUid, "Jane"), contentType: "text/vcard");

        Assert.Equal(HttpStatusCode.NoContent, (await SendDav(dav, "DELETE", url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dav.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Multiget_returns_only_the_requested_contact()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var dav = Factory.DavClient(Email);
        var colUrl = $"/dav/u/{uid}/card/{abId}/";
        await SendDav(dav, "PUT", $"{colUrl}a@x.vcf", body: MinimalVcf("a@x", "A"), contentType: "text/vcard");
        await SendDav(dav, "PUT", $"{colUrl}b@x.vcf", body: MinimalVcf("b@x", "B"), contentType: "text/vcard");

        var doc = await ReadXml(await SendDav(dav, "REPORT", colUrl, body: AddressbookMultigetBody($"{colUrl}a@x.vcf")));
        var withData = doc.Descendants(D + "response")
            .Where(r => r.Descendants(CR + "address-data").Any())
            .Select(r => r.Element(D + "href")?.Value ?? "").ToList();
        Assert.Contains(withData, h => h.Contains("a@x"));
        Assert.DoesNotContain(withData, h => h.Contains("b@x"));
    }
}
