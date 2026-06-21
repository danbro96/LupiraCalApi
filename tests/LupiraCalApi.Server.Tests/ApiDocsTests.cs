using System.Net;
using Xunit;

namespace LupiraCalApi.Server.Tests;

/// <summary>The API exposes its OpenAPI document and the Scalar reference UI (anonymous, per house style).</summary>
public sealed class ApiDocsTests(CalApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var resp = await Factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Scalar_reference_is_served()
    {
        var resp = await Factory.CreateClient().GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
