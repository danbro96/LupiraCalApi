using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Contacts;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>The kinship rules: siblinghood is derived from shared parents (explicit Sibling edges are converted /
/// dissolved when a parent is present), the opt-in inferred read surface, and the one-time normalize sweep.</summary>
public sealed class ContactKinshipTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task<ContactDto> AddRelationAsync(HttpClient api, Guid contactId, Guid toContactId, ContactRelationKind kind)
    {
        var resp = await api.PostAsJsonAsync($"/contacts/{contactId}/relations", new AddContactRelationRequest { ToContactId = toContactId, Kind = kind });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    static async Task<List<ContactRelationEntryDto>> RelationsAsync(HttpClient api, Guid id, bool inferred = false) =>
        (await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{id}/relations?includeInferred={inferred}"))!;

    [Fact]
    public async Task Sibling_with_a_known_parent_is_converted_to_shared_parentage()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");
        var child = await CreateContactAsync(api, ab, "Cara", "Child");
        var sib = await CreateContactAsync(api, ab, "Sam", "Sibling");

        await AddRelationAsync(api, child.Id, parent.Id, ContactRelationKind.Parent);   // child's parent is known
        var afterSibling = await AddRelationAsync(api, child.Id, sib.Id, ContactRelationKind.Sibling);

        // No Sibling edge is stored; the sibling instead gains the shared parent.
        Assert.DoesNotContain(afterSibling.Relations, r => r.Kind == ContactRelationKind.Sibling);
        var sibRaw = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{sib.Id}"))!;
        Assert.Contains(sibRaw.Relations, r => r.ToContactId == parent.Id && r.Kind == ContactRelationKind.Parent);

        // And they now resolve as inferred siblings.
        Assert.Contains(await RelationsAsync(api, child.Id, inferred: true),
            e => e.ContactId == sib.Id && e.Kind == KinshipKind.Sibling && e.Provenance == RelationProvenance.Inferred);
    }

    [Fact]
    public async Task Adding_a_parent_dissolves_existing_sibling_edges()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, ab, "Ann", "A");
        var b = await CreateContactAsync(api, ab, "Bo", "B");
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");

        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Sibling);   // both parentless → explicit edge kept
        Assert.Contains((await api.GetFromJsonAsync<ContactDto>($"/contacts/{a.Id}"))!.Relations, r => r.Kind == ContactRelationKind.Sibling);

        var afterParent = await AddRelationAsync(api, a.Id, parent.Id, ContactRelationKind.Parent);

        Assert.DoesNotContain(afterParent.Relations, r => r.Kind == ContactRelationKind.Sibling);   // edge dissolved
        var bRaw = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{b.Id}"))!;
        Assert.Contains(bRaw.Relations, r => r.ToContactId == parent.Id && r.Kind == ContactRelationKind.Parent);   // b inherited the parent
        Assert.Contains(await RelationsAsync(api, a.Id, inferred: true), e => e.ContactId == b.Id && e.Kind == KinshipKind.Sibling);
    }

    [Fact]
    public async Task Inferred_listing_returns_the_two_generation_closure_only_when_requested()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var gp = await CreateContactAsync(api, ab, "Grand", "Pa");
        var p = await CreateContactAsync(api, ab, "Pat", "Parent");
        var unc = await CreateContactAsync(api, ab, "Uncle", "Bob");
        var a = await CreateContactAsync(api, ab, "Ann", "Focus");
        var cous = await CreateContactAsync(api, ab, "Cory", "Cousin");

        await AddRelationAsync(api, p.Id, gp.Id, ContactRelationKind.Parent);     // p, unc are gp's children (siblings)
        await AddRelationAsync(api, unc.Id, gp.Id, ContactRelationKind.Parent);
        await AddRelationAsync(api, a.Id, p.Id, ContactRelationKind.Parent);       // a is p's child
        await AddRelationAsync(api, cous.Id, unc.Id, ContactRelationKind.Parent);  // cousin is unc's child

        var explicitOnly = await RelationsAsync(api, a.Id);
        Assert.Equal(p.Id, Assert.Single(explicitOnly).ContactId);   // only the explicit parent
        Assert.All(explicitOnly, e => Assert.Equal(RelationProvenance.Explicit, e.Provenance));

        var inferred = await RelationsAsync(api, a.Id, inferred: true);
        KinshipKind Kind(Guid id) => inferred.Single(e => e.ContactId == id).Kind;
        Assert.Equal(KinshipKind.Grandparent, Kind(gp.Id));
        Assert.Equal(KinshipKind.AuntUncle, Kind(unc.Id));
        Assert.Equal(KinshipKind.Cousin, Kind(cous.Id));
        Assert.All(inferred.Where(e => e.ContactId != p.Id), e => Assert.Equal(RelationProvenance.Inferred, e.Provenance));
    }

    [Fact]
    public async Task Normalize_sweep_converts_legacy_violations_and_is_idempotent()
    {
        var api = Factory.ApiClient(Email);
        var uid = await GetMyIdAsync(api);
        var ab = await CreateAddressBookAsync(api);
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");
        var b = await CreateContactAsync(api, ab, "Bo", "B");
        var dav = Factory.DavClient(Email);

        // CardDAV import is exempt from the invariant, so it can create a parent+sibling violation to sweep.
        var vcardUid = "legacy-a@x";
        var vcf = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{vcardUid}\r\nFN:Legacy A\r\nN:Legacy A;;;;\r\n" +
                  $"RELATED;TYPE=parent:urn:uuid:{parent.Id:D}\r\nRELATED;TYPE=sibling:urn:uuid:{b.Id:D}\r\nEND:VCARD\r\n";
        (await SendDav(dav, "PUT", $"/dav/u/{uid}/card/{ab}/{vcardUid}.vcf", body: vcf, contentType: "text/vcard")).EnsureSuccessStatusCode();

        var first = await api.PostAsync($"/contacts/relations/normalize?addressBookId={ab}", null);
        first.EnsureSuccessStatusCode();
        Assert.True(await first.Content.ReadFromJsonAsync<int>() >= 1);

        var second = await api.PostAsync($"/contacts/relations/normalize?addressBookId={ab}", null);
        Assert.Equal(0, await second.Content.ReadFromJsonAsync<int>());   // idempotent

        var bRaw = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{b.Id}"))!;
        Assert.Contains(bRaw.Relations, r => r.ToContactId == parent.Id && r.Kind == ContactRelationKind.Parent);
    }
}
