using System.Net;
using System.Security.Claims;
using LupiraCalApi.Auth;
using LupiraCalApi.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LupiraCalApi.UnitTests;

public class OutboundAuthProviderTests
{
    private const string DavClient = "lupira-dav-svc";

    private static readonly ContactApiOptions Hop = new()
    {
        BaseUrl = "http://contact.test/",
        Audience = "lupira-contact",
        TokenUrl = "https://auth.test/application/o/token/",
        ClientId = "lupira-contact-svc",
        ClientSecret = "svc-secret",
        Scope = "lupira-contact-aud",
    };

    private static HttpContext Context(string? scheme, IEnumerable<Claim>? claims = null, string? authorization = null)
    {
        var ctx = new DefaultHttpContext();
        if (scheme is not null) ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims ?? [], scheme));
        if (authorization is not null) ctx.Request.Headers.Authorization = authorization;
        return ctx;
    }

    private static (OutboundAuthProvider Provider, TokenStubHandler Tokens) Create(HttpContext ctx, bool exchangeConfigured = true)
    {
        var tokens = new TokenStubHandler();
        var exchange = exchangeConfigured
            ? new TokenExchangeOptions { TokenUrl = "https://auth.test/application/o/token/", ClientId = "lupira-cal", ClientSecret = "cal-secret" }
            : new TokenExchangeOptions();
        var provider = new OutboundAuthProvider(
            new HttpContextAccessor { HttpContext = ctx },
            new TokenEndpointClient(new StubHttpClientFactory(tokens)),
            new TokenCache(TimeProvider.System),
            Options.Create(exchange),
            Options.Create(new DavGatewayOptions { ClientId = DavClient }),
            NullLogger<OutboundAuthProvider>.Instance);
        return (provider, tokens);
    }

    [Fact]
    public void Classify_matrix()
    {
        Assert.Equal(InboundIdentity.Service, OutboundAuthProvider.Classify(null, DavClient).Kind);
        Assert.Equal(InboundIdentity.Service, OutboundAuthProvider.Classify(Context(null), DavClient).Kind);
        Assert.Equal(InboundIdentity.Service, OutboundAuthProvider.Classify(
            Context("Bearer", [new Claim("azp", DavClient)], "Bearer dav-token"), DavClient).Kind);
        Assert.Equal(InboundIdentity.Service, OutboundAuthProvider.Classify(
            Context(DevAuthHandler.SchemeName, [new Claim("email", "a@x.test"), new Claim("azp", DavClient)]), DavClient).Kind);
        Assert.Equal(InboundIdentity.Service, OutboundAuthProvider.Classify(Context("Bearer", [new Claim("sub", "a")]), DavClient).Kind);

        var dev = OutboundAuthProvider.Classify(Context(DevAuthHandler.SchemeName, [new Claim("email", "a@x.test")]), DavClient);
        Assert.Equal(InboundIdentity.DevMember, dev.Kind);
        Assert.Equal("a@x.test", dev.DevEmail);

        var member = OutboundAuthProvider.Classify(
            Context("Bearer", [new Claim("sub", "a"), new Claim("exp", "1790000000")], "Bearer member-token"), DavClient);
        Assert.Equal(InboundIdentity.Member, member.Kind);
        Assert.Equal("member-token", member.SubjectToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000), member.SubjectExpiresAt);
    }

    [Fact]
    public async Task Member_exchanges_its_bearer_for_the_hop_audience()
    {
        var (provider, tokens) = Create(Context("Bearer", [new Claim("sub", "a")], "Bearer member-token"));

        var headers = await provider.ResolveHeadersAsync(Hop, default);

        Assert.Equal("Bearer tok-lupira-contact", headers!["Authorization"]);
        var form = Assert.Single(tokens.Forms);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", form["grant_type"]);
        Assert.Equal("lupira-cal", form["client_id"]);
        Assert.Equal("member-token", form["subject_token"]);
        Assert.Equal("lupira-contact", form["audience"]);
    }

    [Fact]
    public async Task Dav_gateway_uses_client_credentials()
    {
        var (provider, tokens) = Create(Context("Bearer", [new Claim("azp", DavClient)], "Bearer dav-token"));

        var headers = await provider.ResolveHeadersAsync(Hop, default);

        Assert.Equal("Bearer tok-lupira-contact-svc", headers!["Authorization"]);
        var form = Assert.Single(tokens.Forms);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal("lupira-contact-svc", form["client_id"]);
    }

    [Fact]
    public async Task Dev_member_propagates_its_email()
    {
        var (provider, tokens) = Create(Context(DevAuthHandler.SchemeName, [new Claim("email", "a@x.test")]));

        var headers = await provider.ResolveHeadersAsync(Hop, default);

        Assert.Equal("a@x.test", headers!["X-Dev-User"]);
        Assert.Empty(tokens.Forms);
    }

    [Fact]
    public async Task Exchange_failure_returns_null_and_never_falls_back_to_the_service_credential()
    {
        var (provider, tokens) = Create(Context("Bearer", [new Claim("sub", "a")], "Bearer member-token"));
        tokens.Respond = _ => (HttpStatusCode.BadRequest, """{"error":"access_denied"}""");

        Assert.Null(await provider.ResolveHeadersAsync(Hop, default));
        Assert.DoesNotContain(tokens.Forms, f => f["grant_type"] == "client_credentials");
    }

    [Fact]
    public async Task Member_without_exchange_config_returns_null()
    {
        var (provider, tokens) = Create(Context("Bearer", [new Claim("sub", "a")], "Bearer member-token"), exchangeConfigured: false);

        Assert.Null(await provider.ResolveHeadersAsync(Hop, default));
        Assert.Empty(tokens.Forms);
    }

    [Fact]
    public async Task Service_without_credentials_falls_back_to_dev_user_then_nothing()
    {
        var (provider, _) = Create(Context(null));

        var dev = await provider.ResolveHeadersAsync(new GeoApiOptions { BaseUrl = "http://geo.test/", DevUser = "svc@x.test" }, default);
        var none = await provider.ResolveHeadersAsync(new GeoApiOptions { BaseUrl = "http://geo.test/" }, default);

        Assert.Equal("svc@x.test", dev!["X-Dev-User"]);
        Assert.Empty(none!);
    }
}
