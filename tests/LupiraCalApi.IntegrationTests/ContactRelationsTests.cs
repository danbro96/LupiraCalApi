using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.Contacts;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Contact-to-contact relations over REST: upsert/remove semantics, the resolved two-way listing with
/// derived inverse kinds, the write-from/read-to authorization rule, and read-side filtering of dangling edges.</summary>
public sealed class ContactRelationsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task<ContactDto> AddRelationAsync(HttpClient api, Guid contactId, Guid toContactId, ContactRelationKind kind, string? label = null)
    {
        var resp = await api.PostAsJsonAsync($"/contacts/{contactId}/relations",
            new AddContactRelationRequest { ToContactId = toContactId, Kind = kind, Label = label });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    [Fact]
    public async Task Add_lists_outgoing_and_the_target_sees_the_derived_inverse()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var y = await CreateContactAsync(api, abId, "Young", "Doe");
        var x = await CreateContactAsync(api, abId, "Old", "Doe");

        // "X is Y's dad" — stored on Y.
        var updated = await AddRelationAsync(api, y.Id, x.Id, ContactRelationKind.Parent, "dad");
        var edge = Assert.Single(updated.Relations);
        Assert.Equal((x.Id, ContactRelationKind.Parent, "dad"), (edge.ToContactId, edge.Kind, edge.Label));
        Assert.NotEqual(y.Etag, updated.Etag);   // relations are part of the canonical vCard

        var fromY = (await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{y.Id}/relations"))!;
        var outgoing = Assert.Single(fromY);
        Assert.Equal((x.Id, ContactRelationKind.Parent, "dad", ContactRelationDirection.Outgoing),
            (outgoing.ContactId, outgoing.Kind, outgoing.Label, outgoing.Direction));

        var fromX = (await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{x.Id}/relations"))!;
        var incoming = Assert.Single(fromX);
        Assert.Equal((y.Id, ContactRelationKind.Child, null, ContactRelationDirection.Incoming),
            (incoming.ContactId, incoming.Kind, incoming.Label, incoming.Direction));
    }

    [Fact]
    public async Task Readd_is_idempotent_and_a_new_label_upserts()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId, "A", "One");
        var b = await CreateContactAsync(api, abId, "B", "Two");

        var first = await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Parent, "dad");
        var identical = await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Parent, "dad");
        Assert.Equal(first.Etag, identical.Etag);   // no event appended, no ETag churn

        var relabeled = await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Parent, "father");
        Assert.NotEqual(first.Etag, relabeled.Etag);
        var edge = Assert.Single(relabeled.Relations);
        Assert.Equal("father", edge.Label);
    }

    [Fact]
    public async Task Remove_deletes_by_target_and_kind_and_a_second_delete_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId, "A", "One");
        var b = await CreateContactAsync(api, abId, "B", "Two");
        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Friend);
        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Colleague);

        var resp = await api.DeleteAsync($"/contacts/{a.Id}/relations/{b.Id}?kind=Friend");
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
        var left = Assert.Single(dto.Relations);
        Assert.Equal(ContactRelationKind.Colleague, left.Kind);

        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/contacts/{a.Id}/relations/{b.Id}?kind=Friend")).StatusCode);
    }

    [Fact]
    public async Task Self_relation_and_unknown_target_are_bad_requests()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId);

        var self = await api.PostAsJsonAsync($"/contacts/{a.Id}/relations",
            new AddContactRelationRequest { ToContactId = a.Id, Kind = ContactRelationKind.Friend });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        var missing = await api.PostAsJsonAsync($"/contacts/{a.Id}/relations",
            new AddContactRelationRequest { ToContactId = Guid.NewGuid(), Kind = ContactRelationKind.Friend });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var b = await CreateContactAsync(api, abId, "B", "Gone");
        await api.DeleteAsync($"/contacts/{b.Id}");
        var deleted = await api.PostAsJsonAsync($"/contacts/{a.Id}/relations",
            new AddContactRelationRequest { ToContactId = b.Id, Kind = ContactRelationKind.Friend });
        Assert.Equal(HttpStatusCode.BadRequest, deleted.StatusCode);
    }

    [Fact]
    public async Task Write_is_required_on_the_from_book_and_read_on_the_to_book()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var aliceBook = await CreateAddressBookAsync(alice);
        var bobBook = await CreateAddressBookAsync(bob, "bobs", "Bobs");
        var x = await CreateContactAsync(alice, aliceBook, "X", "Alice");
        var y = await CreateContactAsync(bob, bobBook, "Y", "Bob");

        // Bob can read (not write) Alice's book: relating FROM her contact is forbidden.
        await alice.PostAsJsonAsync($"/address-books/{aliceBook}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" });
        var fromReadOnly = await bob.PostAsJsonAsync($"/contacts/{x.Id}/relations",
            new AddContactRelationRequest { ToContactId = y.Id, Kind = ContactRelationKind.Friend });
        Assert.Equal(HttpStatusCode.Forbidden, fromReadOnly.StatusCode);

        // Alice has no access to Bob's book: relating TO his contact is forbidden.
        var toInaccessible = await alice.PostAsJsonAsync($"/contacts/{x.Id}/relations",
            new AddContactRelationRequest { ToContactId = y.Id, Kind = ContactRelationKind.Friend });
        Assert.Equal(HttpStatusCode.Forbidden, toInaccessible.StatusCode);

        // Read on the to-book is enough: Bob relates his own contact to Alice's readable one.
        var ok = await bob.PostAsJsonAsync($"/contacts/{y.Id}/relations",
            new AddContactRelationRequest { ToContactId = x.Id, Kind = ContactRelationKind.Friend });
        ok.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Incoming_listing_omits_edges_from_contacts_the_viewer_cannot_read()
    {
        var alice = Factory.ApiClient(Email);
        var bob = Factory.ApiClient("bob@x.test");
        var aliceBook = await CreateAddressBookAsync(alice);
        var bobBook = await CreateAddressBookAsync(bob, "bobs", "Bobs");
        var x = await CreateContactAsync(alice, aliceBook, "X", "Alice");
        var z = await CreateContactAsync(bob, bobBook, "Z", "Bob");

        await alice.PostAsJsonAsync($"/address-books/{aliceBook}/owners", new GrantOwnerRequest { Email = "bob@x.test", Access = "read" });
        (await bob.PostAsJsonAsync($"/contacts/{z.Id}/relations",
            new AddContactRelationRequest { ToContactId = x.Id, Kind = ContactRelationKind.Colleague })).EnsureSuccessStatusCode();

        // Bob sees both sides; Alice can't read Bob's book, so the incoming edge is hidden from her.
        var bobsView = (await bob.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{x.Id}/relations"))!;
        Assert.Contains(bobsView, e => e.ContactId == z.Id && e.Direction == ContactRelationDirection.Incoming);

        var alicesView = (await alice.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{x.Id}/relations"))!;
        Assert.Empty(alicesView);
    }

    [Fact]
    public async Task Deleted_target_is_filtered_from_the_resolved_listing_but_the_raw_edge_stays()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var a = await CreateContactAsync(api, abId, "A", "One");
        var b = await CreateContactAsync(api, abId, "B", "Two");
        await AddRelationAsync(api, a.Id, b.Id, ContactRelationKind.Sibling);

        await api.DeleteAsync($"/contacts/{b.Id}");

        var resolved = (await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{a.Id}/relations"))!;
        Assert.Empty(resolved);

        var raw = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{a.Id}"))!;
        Assert.Equal(b.Id, Assert.Single(raw.Relations).ToContactId);   // no-FK convention: edge kept, filtered on read
    }
}
