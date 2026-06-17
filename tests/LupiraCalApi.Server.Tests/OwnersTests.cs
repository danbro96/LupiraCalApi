using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Calendars;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraCalApi.Server.Tests;

/// <summary>HTTP-level sharing: grant/revoke co-owners on calendars + address books, and the DAV crossover that
/// makes a granted container appear in the grantee's calendar home.</summary>
public sealed class OwnersTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Owner_grants_a_co_owner_who_can_then_list_and_read()
    {
        var alice = Factory.ApiClient("alice@x.test");
        var calId = await CreateCalendarAsync(alice, "fam", "Family");
        var create = await alice.PostAsJsonAsync("/api/items",
            new CreateCalendarItemRequest(calId, "Dinner", null, null, null, false, Start, Start.AddHours(1), "UTC", null, null, null, null, null));
        var item = (await create.Content.ReadFromJsonAsync<CalendarItemDto>())!;

        var grant = await alice.PostAsJsonAsync($"/api/calendars/{calId}/owners", new GrantOwnerRequest("bob@x.test", "owner"));
        grant.EnsureSuccessStatusCode();
        var dto = (await grant.Content.ReadFromJsonAsync<OwnerGrantDto>())!;
        Assert.Equal("calendar", dto.Kind);
        Assert.Equal("bob@x.test", dto.Email);
        Assert.Equal("Owner", dto.Access);

        var bob = Factory.ApiClient("bob@x.test");
        var bobCals = await bob.GetFromJsonAsync<List<ContainerDto>>("/api/calendars");
        Assert.Contains(bobCals!, c => c.Id == calId);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Non_owner_cannot_grant()
    {
        var alice = Factory.ApiClient("alice@x.test");
        var calId = await CreateCalendarAsync(alice, "fam", "Family");

        var carol = Factory.ApiClient("carol@x.test");   // no access to the calendar
        var resp = await carol.PostAsJsonAsync($"/api/calendars/{calId}/owners", new GrantOwnerRequest("eve@x.test", "owner"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Grant_to_unknown_container_is_404_and_bad_access_is_400()
    {
        var alice = Factory.ApiClient("alice@x.test");
        var calId = await CreateCalendarAsync(alice, "fam", "Family");

        var missing = await alice.PostAsJsonAsync($"/api/calendars/{Guid.NewGuid()}/owners", new GrantOwnerRequest("bob@x.test", "owner"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var bad = await alice.PostAsJsonAsync($"/api/calendars/{calId}/owners", new GrantOwnerRequest("bob@x.test", "admin"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Revoke_drops_access_last_owner_is_409_and_non_grantee_is_404()
    {
        var alice = Factory.ApiClient("alice@x.test");
        var calId = await CreateCalendarAsync(alice, "fam", "Family");
        await alice.PostAsJsonAsync($"/api/calendars/{calId}/owners", new GrantOwnerRequest("bob@x.test", "owner"));

        var revoke = await alice.DeleteAsync($"/api/calendars/{calId}/owners?email={Uri.EscapeDataString("bob@x.test")}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        var bob = Factory.ApiClient("bob@x.test");
        Assert.DoesNotContain((await bob.GetFromJsonAsync<List<ContainerDto>>("/api/calendars"))!, c => c.Id == calId);

        var nonGrantee = await alice.DeleteAsync($"/api/calendars/{calId}/owners?email={Uri.EscapeDataString("nobody@x.test")}");
        Assert.Equal(HttpStatusCode.NotFound, nonGrantee.StatusCode);

        var lastOwner = await alice.DeleteAsync($"/api/calendars/{calId}/owners?email={Uri.EscapeDataString("alice@x.test")}");
        Assert.Equal(HttpStatusCode.Conflict, lastOwner.StatusCode);
    }

    [Fact]
    public async Task Address_book_grant_lets_a_member_read_a_contact_then_revoke_removes_it()
    {
        var alice = Factory.ApiClient("alice@x.test");
        var bookId = await CreateAddressBookAsync(alice, "fam", "Family");
        var contact = await CreateContactAsync(alice, bookId);

        var grant = await alice.PostAsJsonAsync($"/api/address-books/{bookId}/owners", new GrantOwnerRequest("bob@x.test", "read"));
        grant.EnsureSuccessStatusCode();

        var bob = Factory.ApiClient("bob@x.test");
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/contacts/{contact.Id}")).StatusCode);

        var revoke = await alice.DeleteAsync($"/api/address-books/{bookId}/owners?email={Uri.EscapeDataString("bob@x.test")}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/api/contacts/{contact.Id}")).StatusCode);
    }

    [Fact]
    public async Task Granted_calendar_appears_in_the_grantees_dav_home()
    {
        var alice = Factory.ApiClient("alice@x.test");
        var calId = await CreateCalendarAsync(alice, "fam", "Family");
        await alice.PostAsJsonAsync($"/api/calendars/{calId}/owners", new GrantOwnerRequest("bob@x.test", "owner"));

        var bob = Factory.ApiClient("bob@x.test");
        var bobId = await GetMyIdAsync(bob);
        var bobDav = Factory.DavClient("bob@x.test");
        var doc = await ReadXml(await SendDav(bobDav, "PROPFIND", $"/dav/u/{bobId}/cal/", depth: "1"));
        Assert.Contains(doc.Descendants(D + "href"), h => h.Value.Contains(calId.ToString()));
    }
}
