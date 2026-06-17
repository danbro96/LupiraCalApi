using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LupiraCalApi.Dtos.CalendarItems;
using Xunit;

namespace LupiraCalApi.Server.Tests;

public sealed class CalendarItemsRestTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    private static async Task<CalendarItemDto> CreateAsync(HttpClient api, Guid calId, string title = "Mtg", string[]? tags = null)
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var resp = await api.PostAsJsonAsync("/api/items", new CreateCalendarItemRequest(calId, title, null, null, null, false, start, start.AddHours(1), "UTC", null, null, null, null, tags));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task Update_changes_fields()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateAsync(api, calId);

        var upd = await api.PutAsJsonAsync($"/api/items/{item.Id}", new UpdateCalendarItemRequest("Renamed", "new desc", null, null, null, null, null, null));
        upd.EnsureSuccessStatusCode();

        var got = await api.GetFromJsonAsync<CalendarItemDto>($"/api/items/{item.Id}");
        Assert.Equal("Renamed", got!.Title);
        Assert.Equal("new desc", got.Description);
    }

    [Fact]
    public async Task Delete_then_get_is_not_found()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateAsync(api, calId);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/items/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Metadata_is_merged()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        var item = await CreateAsync(api, calId);

        JsonNode patch = new JsonObject { ["trip"] = "tokyo-2026" };
        var resp = await api.PostAsJsonAsync($"/api/items/{item.Id}/metadata", patch);
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
        Assert.Equal("tokyo-2026", dto.Metadata?["trip"]?.GetValue<string>());
    }

    [Fact]
    public async Task Search_filters_by_tag_and_text()
    {
        var api = Factory.ApiClient(Email);
        var calId = await CreateCalendarAsync(api);
        await CreateAsync(api, calId, "Standup", ["work"]);
        await CreateAsync(api, calId, "Dentist", ["health"]);

        var from = Uri.EscapeDataString(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).ToString("o"));
        var to = Uri.EscapeDataString(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero).ToString("o"));

        var byTag = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/api/items?tag=work&from={from}&to={to}");
        Assert.Contains(byTag!, o => o.Title == "Standup");
        Assert.DoesNotContain(byTag!, o => o.Title == "Dentist");

        var byText = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/api/items?query=Dentist&from={from}&to={to}");
        Assert.Contains(byText!, o => o.Title == "Dentist");
        Assert.DoesNotContain(byText!, o => o.Title == "Standup");
    }
}
