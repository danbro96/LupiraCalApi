using System.Net.Http.Headers;
using System.Security.Claims;
using Lupira.Auth.DevUser;
using Lupira.Clients.ServiceTokens;
using LupiraCalApi.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Clients;

/// <summary>
/// One rule for every outbound hop: a member behind the request → exchange their bearer for the hop's audience
/// (RFC 8693) and act as them, so the target's ACL applies; no member (DAV gateway, background) → client
/// credentials. A failed exchange never falls back to the service credential — that would silently re-widen the
/// ACL — it returns false and the caller fails open.
/// </summary>
public sealed class OutboundAuthProvider(
    IHttpContextAccessor http,
    TokenEndpointClient tokens,
    TokenCache cache,
    ServiceTokenProvider serviceTokens,
    IOptions<TokenExchangeOptions> exchange,
    IOptions<DavGatewayOptions> davGateway,
    ILogger<OutboundAuthProvider> logger)
{
    private const string DevUserHeader = "X-Dev-User";

    public static InboundCaller Classify(HttpContext? ctx, string? davGatewayClientId)
    {
        var user = ctx?.User;
        if (user?.Identity?.IsAuthenticated != true) return new InboundCaller(InboundIdentity.Service);
        if (DavGatewayOptions.IsGateway(user, davGatewayClientId)) return new InboundCaller(InboundIdentity.Service);
        if (user.Identity.AuthenticationType == DevAuthenticationBuilderExtensions.DefaultScheme)
            return new InboundCaller(InboundIdentity.DevMember, DevEmail: user.FindFirstValue("email"));

        var header = ctx!.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(JwtBearerDefaults.AuthenticationScheme + " ", StringComparison.OrdinalIgnoreCase))
            return new InboundCaller(InboundIdentity.Service);
        DateTimeOffset? exp = long.TryParse(user.FindFirstValue("exp"), out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        return new InboundCaller(InboundIdentity.Member, header[(JwtBearerDefaults.AuthenticationScheme.Length + 1)..].Trim(), exp);
    }

    /// <summary>Authenticates <paramref name="req"/> for the hop; false when no credential could be obtained (the
    /// caller skips the call and fails open).</summary>
    public async Task<bool> TryAuthorizeAsync(HttpRequestMessage req, IOutboundHopOptions hop, CancellationToken ct)
    {
        var caller = Classify(http.HttpContext, davGateway.Value.ClientId);
        switch (caller.Kind)
        {
            case InboundIdentity.Member:
                return SetBearer(req, await ExchangeAsync(caller, hop, ct));
            case InboundIdentity.DevMember:
                req.Headers.TryAddWithoutValidation(DevUserHeader, caller.DevEmail ?? string.Empty);
                return true;
            default:
                return await AuthorizeAsServiceAsync(req, hop, ct);
        }
    }

    private async Task<string?> ExchangeAsync(InboundCaller caller, IOutboundHopOptions hop, CancellationToken ct)
    {
        var opts = exchange.Value;
        if (!opts.IsConfigured || string.IsNullOrWhiteSpace(hop.Audience))
        {
            logger.LogWarning("Token exchange to {Audience} not configured; outbound call skipped.", hop.Audience);
            return null;
        }

        try
        {
            return await cache.GetOrMintAsync(
                TokenCache.ExchangeKey(caller.SubjectToken!, hop.Audience!),
                token => tokens.ExchangeAsync(opts, caller.SubjectToken!, hop.Audience!, token),
                caller.SubjectExpiresAt, ct);
        }
        catch (TokenEndpointException ex)
        {
            logger.Log(ex.Kind == TokenErrorKind.InvalidClient ? LogLevel.Error : LogLevel.Warning,
                "Token exchange to {Audience} failed: {Kind} ({StatusCode}) {Description}", hop.Audience, ex.Kind, ex.StatusCode, ex.Description);
            return null;
        }
    }

    private async Task<bool> AuthorizeAsServiceAsync(HttpRequestMessage req, IOutboundHopOptions hop, CancellationToken ct)
    {
        try
        {
            await serviceTokens.ApplyAsync(req, hop, ct);
            return true;
        }
        catch (TokenEndpointException ex)
        {
            logger.LogWarning("Client-credentials token for {ClientId} failed: {Kind} ({StatusCode}) {Description}",
                hop.ClientId, ex.Kind, ex.StatusCode, ex.Description);
            return false;
        }
    }

    private static bool SetBearer(HttpRequestMessage req, string? token)
    {
        if (token is null) return false;
        req.Headers.Authorization = new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, token);
        return true;
    }
}
