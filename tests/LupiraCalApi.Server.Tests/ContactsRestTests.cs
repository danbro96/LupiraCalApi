using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Dtos.Contacts;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class ContactsRestTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Create_then_get_and_list()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId, "Jane", "Doe", "jane@x.test");
        Assert.Equal("Jane Doe", contact.DisplayName);

        var got = await api.GetFromJsonAsync<ContactDto>($"/api/contacts/{contact.Id}");
        Assert.Equal(contact.Id, got!.Id);

        var list = await api.GetFromJsonAsync<List<ContactDto>>($"/api/contacts?addressBookId={abId}");
        Assert.Contains(list!, c => c.Id == contact.Id);
    }

    [Fact]
    public async Task Query_matches_by_name()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        await CreateContactAsync(api, abId, "Zaphod", "Beeblebrox");
        await CreateContactAsync(api, abId, "Arthur", "Dent");

        var hits = await api.GetFromJsonAsync<List<ContactDto>>("/api/contacts?query=Zaphod");
        Assert.Contains(hits!, c => c.DisplayName.Contains("Zaphod"));
        Assert.DoesNotContain(hits!, c => c.DisplayName.Contains("Arthur"));
    }

    [Fact]
    public async Task Delete_then_get_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/contacts/{contact.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/contacts/{contact.Id}")).StatusCode);
    }
}
