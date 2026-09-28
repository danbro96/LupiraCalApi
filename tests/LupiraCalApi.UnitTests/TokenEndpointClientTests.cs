using System.Net;
using LupiraCalApi.Clients;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class TokenEndpointClientTests
{
    private static readonly TokenExchangeOptions Exchange = new()
    {
        TokenUrl = "https://auth.test/application/o/token/",
        ClientId = "lupira-cal",
        ClientSecret = "s3cret",
    };

    private static readonly GeoApiOptions Hop = new()
    {
        TokenUrl = "https://auth.test/application/o/token/",
        ClientId = "lupira-geo-svc",
        ClientSecret = "svc-secret",
        Scope = "lupira-geo-aud",
    };

    [Fact]
    public async Task Exchange_posts_the_rfc8693_form()
    {
        var handler = new TokenStubHandler();
        var issued = await new TokenEndpointClient(new StubHttpClientFactory(handler)).ExchangeAsync(Exchange, "member-token", "lupira-contact", default);

        var form = Assert.Single(handler.Forms);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", form["grant_type"]);
        Assert.Equal("lupira-cal", form["client_id"]);
        Assert.Equal("s3cret", form["client_secret"]);
        Assert.Equal("member-token", form["subject_token"]);
        Assert.Equal("urn:ietf:params:oauth:token-type:access_token", form["subject_token_type"]);
        Assert.Equal("lupira-contact", form["audience"]);
        Assert.Equal("openid profile email", form["scope"]);
        Assert.Equal("tok-lupira-contact", issued.AccessToken);
        Assert.Equal(TimeSpan.FromSeconds(300), issued.ExpiresIn);
    }

    [Fact]
    public async Task Client_credentials_posts_the_hop_credentials_and_scope()
    {
        var handler = new TokenStubHandler();
        await new TokenEndpointClient(new StubHttpClientFactory(handler)).ClientCredentialsAsync(Hop, default);

        var form = Assert.Single(handler.Forms);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal("lupira-geo-svc", form["client_id"]);
        Assert.Equal("lupira-geo-aud", form["scope"]);
    }

    [Theory]
    [InlineData("access_denied", TokenErrorKind.AccessDenied)]
    [InlineData("invalid_grant", TokenErrorKind.InvalidGrant)]
    [InlineData("invalid_target", TokenErrorKind.InvalidTarget)]
    [InlineData("invalid_client", TokenErrorKind.InvalidClient)]
    [InlineData("something_else", TokenErrorKind.Other)]
    public async Task Maps_rfc_error_bodies(string error, TokenErrorKind kind)
    {
        var handler = new TokenStubHandler { Respond = _ => (HttpStatusCode.BadRequest, $$"""{"error":"{{error}}","error_description":"nope"}""") };

        var ex = await Assert.ThrowsAsync<TokenEndpointException>(
            () => new TokenEndpointClient(new StubHttpClientFactory(handler)).ExchangeAsync(Exchange, "t", "lupira-contact", default));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("nope", ex.Description);
    }

    [Fact]
    public async Task Non_json_error_is_unavailable()
    {
        var handler = new TokenStubHandler { Respond = _ => (HttpStatusCode.BadGateway, "<html>502</html>") };

        var ex = await Assert.ThrowsAsync<TokenEndpointException>(
            () => new TokenEndpointClient(new StubHttpClientFactory(handler)).ExchangeAsync(Exchange, "t", "lupira-contact", default));

        Assert.Equal(TokenErrorKind.Unavailable, ex.Kind);
    }
}
