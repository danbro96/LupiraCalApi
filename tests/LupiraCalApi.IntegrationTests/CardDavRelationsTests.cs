using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>vCard RELATED over CardDAV: PUT round-trip byte-identity, ETag coherence with REST-side relation edits,
/// wholesale replace on PUT, and the store-dangling/filter-on-read convention for unresolvable targets.</summary>
public sealed class CardDavRelationsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static string VcfWithRelated(string uid, string fullName, string relatedLine) =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{uid}\r\nFN:{fullName}\r\nN:{fullName};;;;\r\n{relatedLine}\r\nEND:VCARD\r\n";

    static async Task<ContactDto> FindByExternalIdAsync(HttpClient api, Guid abId, string externalId)
    {
        var list = await api.GetFromJsonAsync<List<ContactDto>>($"/contacts?addressBookId={abId}");
        return list!.Single(c => c.ExternalId == externalId);
    }

    [Fact]
    public async Task Put_with_related_round_trips_byte_identical_and_shows_over_rest()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var target = await CreateContactAsync(api, abId, "Old", "Doe");
        var dav = Factory.DavClient(Email);

        var vcardUid = "rel-1@x";
        var vcf = VcfWithRelated(vcardUid, "Young Doe", $"RELATED;TYPE=parent;X-LUPIRA-LABEL=dad:urn:uuid:{target.Id:D}");
        var url = $"/dav/u/{uid}/card/{abId}/{vcardUid}.vcf";

        var put = await SendDav(dav, "PUT", url, body: vcf, contentType: "text/vcard");
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);

        var get = await dav.GetAsync(url);
        Assert.Equal(vcf, await get.Content.ReadAsStringAsync());
        Assert.Equal(put.Headers.ETag?.Tag, get.Headers.ETag?.Tag);

        var contact = await FindByExternalIdAsync(api, abId, vcardUid);
        var edge = Assert.Single(contact.Relations);
        Assert.Equal((target.Id, ContactRelationKind.Parent, "dad"), (edge.ToContactId, edge.Kind, edge.Label));
    }

    [Fact]
    public async Task Rest_added_relation_appears_in_dav_get_with_the_new_etag()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId, "A", "One");
        var b = await CreateContactAsync(api, abId, "B", "Two");
        var dav = Factory.DavClient(Email);

        var resp = await api.PostAsJsonAsync($"/contacts/{a.Id}/relations",
            new AddContactRelationRequest { ToContactId = b.Id, Kind = ContactRelationKind.Spouse, Label = "wife" });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;

        var get = await dav.GetAsync($"/dav/u/{uid}/card/{abId}/{a.ExternalId}.vcf");
        var body = await get.Content.ReadAsStringAsync();
        Assert.Contains($"RELATED;TYPE=spouse;X-LUPIRA-LABEL=wife:urn:uuid:{b.Id:D}\r\n", body);
        Assert.Equal($"\"{dto.Etag}\"", get.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_without_related_clears_relations()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var target = await CreateContactAsync(api, abId, "Old", "Doe");
        var dav = Factory.DavClient(Email);

        var vcardUid = "rel-2@x";
        var url = $"/dav/u/{uid}/card/{abId}/{vcardUid}.vcf";
        await SendDav(dav, "PUT", url,
            body: VcfWithRelated(vcardUid, "Kid", $"RELATED;TYPE=parent:urn:uuid:{target.Id:D}"), contentType: "text/vcard");
        Assert.Single((await FindByExternalIdAsync(api, abId, vcardUid)).Relations);

        await SendDav(dav, "PUT", url, body: MinimalVcf(vcardUid, "Kid"), contentType: "text/vcard");
        Assert.Empty((await FindByExternalIdAsync(api, abId, vcardUid)).Relations);
    }

    [Fact]
    public async Task Put_with_unresolvable_target_stores_and_reemits_but_hides_from_the_resolved_listing()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var abId = await CreateAddressBookAsync(api);
        var dav = Factory.DavClient(Email);

        var ghost = Guid.NewGuid();   // never synced in
        var vcardUid = "rel-3@x";
        var url = $"/dav/u/{uid}/card/{abId}/{vcardUid}.vcf";
        var vcf = VcfWithRelated(vcardUid, "Orphan", $"RELATED;TYPE=friend:urn:uuid:{ghost:D}");

        await SendDav(dav, "PUT", url, body: vcf, contentType: "text/vcard");

        var get = await dav.GetAsync(url);
        Assert.Equal(vcf, await get.Content.ReadAsStringAsync());   // dangling edge survives the round-trip

        var contact = await FindByExternalIdAsync(api, abId, vcardUid);
        Assert.Equal(ghost, Assert.Single(contact.Relations).ToContactId);
        var resolved = await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{contact.Id}/relations");
        Assert.Empty(resolved!);
    }
}
