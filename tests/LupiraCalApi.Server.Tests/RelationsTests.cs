using System.Net;
using System.Net.Http.Json;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Relations;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class RelationsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<Guid> CreateItemAsync(HttpClient api, Guid calId)
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var resp = await api.PostAsJsonAsync("/api/items", new CreateCalendarItemRequest(calId, "Mtg", null, null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, null));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!.Id;
    }

    [Fact]
    public async Task Link_list_and_reverse_lookup()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var itemId = await CreateItemAsync(api, calId);

        var link = await api.PostAsJsonAsync($"/api/items/{itemId}/relations", new CreateRelationRequest("task", "task-123", "derived-from", null));
        link.EnsureSuccessStatusCode();
        Assert.Equal("task-123", (await link.Content.ReadFromJsonAsync<RelationDto>())!.ToRef);

        var list = await api.GetFromJsonAsync<List<RelationDto>>($"/api/items/{itemId}/relations");
        Assert.Contains(list!, r => r.ToRef == "task-123");

        var reverse = await api.GetFromJsonAsync<List<CalendarItemDto>>("/api/relations?toKind=task&toRef=task-123");
        Assert.Contains(reverse!, i => i.Id == itemId);
    }

    [Fact]
    public async Task Link_on_a_missing_item_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.PostAsJsonAsync($"/api/items/{Guid.NewGuid()}/relations", new CreateRelationRequest("task", "x", "derived-from", null));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
