using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Dtos.Contacts;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class ContactGroupsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Create_list_member_rename_delete_lifecycle()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var contact = await CreateContactAsync(api, abId);

        var created = await api.PostAsync($"/api/address-books/{abId}/groups?kind=organization&name=Acme", null);
        created.EnsureSuccessStatusCode();
        var group = (await created.Content.ReadFromJsonAsync<ContactGroupDto>())!;
        Assert.Equal("Organization", group.Kind);
        Assert.Equal("Acme", group.Name);

        var list = await api.GetFromJsonAsync<List<ContactGroupDto>>($"/api/address-books/{abId}/groups");
        Assert.Contains(list!, g => g.Id == group.Id);

        var added = await api.PostAsync($"/api/groups/{group.Id}/members?contactId={contact.Id}", null);
        added.EnsureSuccessStatusCode();
        Assert.Contains(contact.Id, (await added.Content.ReadFromJsonAsync<ContactGroupDto>())!.Members);

        var removed = await api.DeleteAsync($"/api/groups/{group.Id}/members/{contact.Id}");
        removed.EnsureSuccessStatusCode();
        Assert.DoesNotContain(contact.Id, (await removed.Content.ReadFromJsonAsync<ContactGroupDto>())!.Members);

        var renamed = await api.PutAsync($"/api/groups/{group.Id}?name=AcmeCorp", null);
        renamed.EnsureSuccessStatusCode();
        Assert.Equal("AcmeCorp", (await renamed.Content.ReadFromJsonAsync<ContactGroupDto>())!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/groups/{group.Id}")).StatusCode);
        var after = await api.GetFromJsonAsync<List<ContactGroupDto>>($"/api/address-books/{abId}/groups");
        Assert.DoesNotContain(after!, g => g.Id == group.Id);
    }
}
