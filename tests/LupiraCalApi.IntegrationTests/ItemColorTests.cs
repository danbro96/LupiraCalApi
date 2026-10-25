using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lupira.Primitives;
using Lupira.Sync;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Sync;
using ModelContextProtocol.Client;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>An item's own colour across REST, occurrence search, the sync feed, series splits, the DAV seam and MCP.</summary>
public sealed class ItemColorTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<CalendarItemDto> CreateAsync(HttpClient api, Guid cal, ItemColor? color, string? rule = null)
    {
        var resp = await api.PostAsJsonAsync("/items", new CreateCalendarItemRequest
        {
            CalendarId = cal, Title = "Gym", StartsAt = Start, EndsAt = Start.AddHours(1), StartTimezone = "UTC", RecurrenceRule = rule, Color = color,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    private static async Task<CalendarItemDto> UpdateAsync(HttpClient api, Guid id, UpdateCalendarItemRequest r)
    {
        var resp = await api.PutAsJsonAsync($"/items/{id}", r);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CalendarItemDto>())!;
    }

    [Fact]
    public async Task Create_then_get_carries_the_colour_and_an_uncoloured_item_has_none()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);

        var coloured = await CreateAsync(api, cal, ItemColor.Teal);
        var plain = await CreateAsync(api, cal, null);

        Assert.Equal(ItemColor.Teal, (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{coloured.Id}"))!.Color);
        Assert.Null((await api.GetFromJsonAsync<CalendarItemDto>($"/items/{plain.Id}"))!.Color);
        Assert.Contains("\"color\":\"Teal\"", await api.GetStringAsync($"/items/{coloured.Id}"));
    }

    [Fact]
    public async Task Update_keeps_the_colour_unless_provided_and_a_provided_null_resets_it()
    {
        var api = Factory.ApiClient(Email);
        var item = await CreateAsync(api, await CreateCalendarAsync(api), ItemColor.Green);

        Assert.Equal(ItemColor.Green, (await UpdateAsync(api, item.Id, new UpdateCalendarItemRequest { Title = "Renamed" })).Color);
        Assert.Equal(ItemColor.Purple, (await UpdateAsync(api, item.Id, new UpdateCalendarItemRequest { Color = ItemColor.Purple })).Color);
        Assert.Null((await UpdateAsync(api, item.Id, new UpdateCalendarItemRequest { ColorProvided = true })).Color);
    }

    [Fact]
    public async Task An_unknown_colour_name_is_a_bad_request()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var body = $$"""{"calendarId":"{{cal}}","title":"Gym","startsAt":"2026-07-01T09:00:00Z","color":"Magenta"}""";

        var resp = await api.PostAsync("/items", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Every_occurrence_in_a_search_carries_the_colour()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        await CreateAsync(api, cal, ItemColor.Orange, "FREQ=DAILY;COUNT=3");

        var occurrences = await api.GetFromJsonAsync<List<CalendarItemOccurrenceDto>>($"/items?calendarId={cal}&from=2026-07-01T00:00:00Z&to=2026-07-05T00:00:00Z");

        Assert.Equal(3, occurrences!.Count);
        Assert.All(occurrences, o => Assert.Equal(ItemColor.Orange, o.Color));
    }

    [Fact]
    public async Task The_sync_feed_carries_the_colour()
    {
        var api = Factory.ApiClient(Email);
        var item = await CreateAsync(api, await CreateCalendarAsync(api), ItemColor.Indigo);

        var resp = await api.GetAsync("/sync/items");
        resp.EnsureSuccessStatusCode();
        var page = (await resp.Content.ReadFromJsonAsync<SyncPage<ItemSyncChange>>(Json))!;

        Assert.Equal(ItemColor.Indigo, Assert.Single(page.Changed, c => c.Item.Id == item.Id).Item.Color);
    }

    [Fact]
    public async Task A_split_continuation_inherits_the_colour_unless_changed()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var kept = await CreateAsync(api, cal, ItemColor.Brown, "FREQ=DAILY");
        var recoloured = await CreateAsync(api, cal, ItemColor.Brown, "FREQ=DAILY");

        async Task<SeriesSplitDto> SplitAsync(Guid id, string key, UpdateCalendarItemRequest changes)
        {
            var resp = await api.PostAsJsonAsync($"/items/{id}/occurrences/2026-07-03T09:00:00Z/following",
                new ChangeFollowingOccurrencesRequest { Changes = changes, SourceKey = key });
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<SeriesSplitDto>())!;
        }

        var inherited = await SplitAsync(kept.Id, "split-kept", new UpdateCalendarItemRequest { Title = "Later" });
        var changed = await SplitAsync(recoloured.Id, "split-recoloured", new UpdateCalendarItemRequest { Color = ItemColor.Yellow });

        Assert.Equal((ItemColor.Brown, ItemColor.Brown), (inherited.Earlier!.Color, inherited.Following!.Color));
        Assert.Equal((ItemColor.Brown, ItemColor.Yellow), (changed.Earlier!.Color, changed.Following!.Color));
    }

    [Fact]
    public async Task The_dav_backend_renders_the_colour_and_a_recolour_changes_the_etag()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var item = await CreateAsync(api, cal, null);

        var plain = await GetIcsBackendAsync(api, Email, cal, item.ExternalId);
        Assert.DoesNotContain("COLOR", await plain.Content.ReadAsStringAsync());
        await UpdateAsync(api, item.Id, new UpdateCalendarItemRequest { Color = ItemColor.Blue });

        var coloured = await GetIcsBackendAsync(api, Email, cal, item.ExternalId);
        coloured.EnsureSuccessStatusCode();
        Assert.Contains("COLOR:royalblue\r\n", await coloured.Content.ReadAsStringAsync());
        Assert.NotEqual(plain.Headers.ETag!.Tag, coloured.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task A_dav_put_with_a_colour_sets_it_and_one_without_keeps_it()
    {
        var api = Factory.ApiClient(Email);
        var cal = await CreateCalendarAsync(api);
        var plain = MinimalIcs("evt-c@x", "Standup", Start);
        var coloured = plain.Replace("SUMMARY:Standup\r\n", "SUMMARY:Standup\r\nCOLOR:DeepPink\r\n");
        var id = DeterministicGuid.From("evt-c@x");

        Assert.Equal(HttpStatusCode.Created, (await PutIcsBackendAsync(api, Email, cal, "evt-c@x", coloured)).StatusCode);
        Assert.Equal(ItemColor.Pink, (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{id}"))!.Color);

        var again = await PutIcsBackendAsync(api, Email, cal, "evt-c@x", plain.Replace("SUMMARY:Standup", "SUMMARY:Standup moved"));
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        var got = (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{id}"))!;
        Assert.Equal(("Standup moved", ItemColor.Pink), (got.Title, got.Color));
    }

    [Fact]
    public async Task The_agent_recolours_and_resets_an_item_over_mcp()
    {
        var api = Factory.ApiClient(Email);
        var item = await CreateAsync(api, await CreateCalendarAsync(api), null);
        var http = Factory.ApiClient(Email);
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true));

        async Task<ItemColor?> UpdateOverMcpAsync(Dictionary<string, object?> request)
        {
            var result = await mcp.CallToolAsync("update_item", new Dictionary<string, object?> { ["itemId"] = item.Id, ["request"] = request });
            Assert.NotEqual(true, result.IsError);
            return (await api.GetFromJsonAsync<CalendarItemDto>($"/items/{item.Id}"))!.Color;
        }

        Assert.Equal(ItemColor.Graphite, await UpdateOverMcpAsync(new() { ["color"] = "Graphite" }));
        Assert.Equal(ItemColor.Graphite, await UpdateOverMcpAsync(new() { ["title"] = "Renamed" }));
        Assert.Null(await UpdateOverMcpAsync(new() { ["color"] = null, ["colorProvided"] = true }));
    }
}
