using System.Net.Http.Json;
using Lupira.Primitives;
using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using LupiraCalApi.Core.Dtos.Calendars;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

[Collection("integration")]
public sealed class McpToolArgumentsTests(CalApiTestFactory factory) : McpStrictArgumentsTests
{
    protected override HttpClient CreateAuthenticatedClient() => factory.ApiClient("alice@x.test");

    protected override string DeclaredToolName => "list_calendars";

    public override Task InitializeAsync() => factory.ResetAsync();

    [Fact]
    public async Task Misnamed_batch_item_field_is_rejected_with_its_path()
    {
        await using var mcp = await ConnectAsync();
        var item = new Dictionary<string, object?> { ["bogus"] = 1 };
        var result = await mcp.CallToolAsync("create_items_batch",
            new Dictionary<string, object?> { ["request"] = new Dictionary<string, object?> { ["items"] = new[] { item } } });

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("unknown request.items[0].bogus", text);
    }

    [Fact]
    public async Task Non_object_metadata_is_rejected()
    {
        await using var mcp = await ConnectAsync();
        foreach (var json in new[] { "[1,2]", "null", "\"x\"", "not json {" })
        {
            var result = await mcp.CallToolAsync("attach_metadata",
                new Dictionary<string, object?> { ["itemId"] = Guid.NewGuid(), ["metadataJson"] = json });

            Assert.True(result.IsError, json);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Contains("must be a JSON object", text);
        }
    }

    [Fact]
    public async Task Subscription_tools_list_subscriptions_and_read_an_event()
    {
        var api = factory.ApiClient("alice@x.test");
        var created = await api.PostAsJsonAsync("/calendars", new CreateCalendarRequest { Slug = "work", Type = "calendar", DefaultTimezone = "UTC" });
        var cal = (await created.Content.ReadFromJsonAsync<ContainerDto>())!.Id;
        var start = new DateTimeOffset(2026, 9, 7, 16, 0, 0, TimeSpan.Zero);
        var sub = await SubscriptionSeed.SeedAsync(factory, cal, SubscriptionSeed.Feed(SubscriptionSeed.Event("m@feed", "Match", start, description: "Bortamatch")), "Klubben");
        var eventId = DeterministicGuid.From($"subscription:{sub.Id:N}:m@feed");
        await using var mcp = await ConnectAsync();

        var listed = await mcp.CallToolAsync("list_subscriptions", new Dictionary<string, object?> { ["calendarId"] = cal });
        Assert.NotEqual(true, listed.IsError);
        Assert.Contains("Klubben", Assert.IsType<TextContentBlock>(Assert.Single(listed.Content)).Text);

        var read = await mcp.CallToolAsync("get_subscribed_event", new Dictionary<string, object?>
        {
            ["calendarId"] = cal,
            ["subscriptionId"] = sub.Id,
            ["eventId"] = eventId,
        });
        Assert.NotEqual(true, read.IsError);
        Assert.Contains("Bortamatch", Assert.IsType<TextContentBlock>(Assert.Single(read.Content)).Text);
    }
}
