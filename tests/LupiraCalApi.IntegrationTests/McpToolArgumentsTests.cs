using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

/// <summary>Every tool refuses an argument its schema doesn't declare, before it runs (so this has no side effects).
/// The rules themselves are tested in LupiraGeoApi, which holds the reference copy of StrictToolArguments.</summary>
public sealed class McpToolArgumentsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    private async Task<McpClient> ConnectAsync()
    {
        var http = Factory.ApiClient("alice@x.test");
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    [Fact]
    public async Task Every_tool_rejects_an_undeclared_argument()
    {
        await using var mcp = await ConnectAsync();
        var tools = await mcp.ListToolsAsync();
        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            var result = await mcp.CallToolAsync(tool.Name, new Dictionary<string, object?> { ["__undeclared"] = 1 });

            Assert.True(result.IsError, tool.Name);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.StartsWith($"Invalid arguments for '{tool.Name}': unknown __undeclared", text);
        }
    }

    [Fact]
    public async Task Declared_arguments_reach_the_tool()
    {
        await using var mcp = await ConnectAsync();
        var result = await mcp.CallToolAsync("list_calendars", new Dictionary<string, object?>());

        Assert.NotEqual(true, result.IsError);
    }

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
}
