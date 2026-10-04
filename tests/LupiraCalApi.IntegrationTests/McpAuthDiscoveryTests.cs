using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraCalApi.IntegrationTests;

[Collection("integration")]
public sealed class McpAuthDiscoveryTests(CalApiTestFactory factory) : McpResourceMetadataTests
{
    protected override HttpClient CreateAnonymousClient() => factory.AnonymousClient();

    protected override string Issuer => factory.Authority!;

    public override Task InitializeAsync() => factory.ResetAsync();
}
