using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
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
}
