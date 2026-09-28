using System.Security.Claims;
using LupiraCalApi.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Clients;

/// <summary>
/// One rule for every outbound hop: a member behind the request → exchange their bearer for the hop's audience
/// (RFC 8693) and act as them, so the target's ACL applies; no member (DAV gateway, background) → client
/// credentials. A failed exchange never falls back to the service credential — that would silently re-widen the
/// ACL — it returns null and the caller fails open.
/// </summary>
public sealed class OutboundAuthProvider(
    IHttpContextAccessor http,
    TokenEndpointClient tokens,
    TokenCache cache,
    IOptions<TokenExchangeOptions> exchange,
    IOptions<DavGatewayOptions> davGateway,
    ILogger<OutboundAuthProvider> logger)
{
    public static InboundCaller Classify(HttpContext? ctx, string? davGatewayClientId)
    {
        var user = ctx?.User;
        if (user?.Identity?.IsAuthenticated != true) return new InboundCaller(InboundIdentity.Service);
        if (davGatewayClientId is not null && user.HasClaim("azp", davGatewayClientId)) return new InboundCaller(InboundIdentity.Service);
        if (user.Identity.AuthenticationType == DevAuthHandler.SchemeName)
            return new InboundCaller(InboundIdentity.DevMember, DevEmail: user.FindFirstValue("email"));

        var header = ctx!.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(JwtBearerDefaults.AuthenticationScheme + " ", StringComparison.OrdinalIgnoreCase))
            return new InboundCaller(InboundIdentity.Service);
        DateTimeOffset? exp = long.TryParse(user.FindFirstValue("exp"), out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        return new InboundCaller(InboundIdentity.Member, header[(JwtBearerDefaults.AuthenticationScheme.Length + 1)..].Trim(), exp);
    }

    /// <summary>The headers that authenticate one outbound call, or null when no credential could be obtained (the
    /// caller skips the call and fails open).</summary>
    public async Task<IReadOnlyDictionary<string, string>?> ResolveHeadersAsync(IOutboundHopOptions hop, CancellationToken ct)
    {
        var caller = Classify(http.HttpContext, davGateway.Value.ClientId);
        return caller.Kind switch
        {
            InboundIdentity.Member => await MemberAsync(caller, hop, ct),
            InboundIdentity.DevMember => new Dictionary<string, string> { ["X-Dev-User"] = caller.DevEmail ?? string.Empty },
            _ => await ServiceAsync(hop, ct),
        };
    }

    private async Task<IReadOnlyDictionary<string, string>?> MemberAsync(InboundCaller caller, IOutboundHopOptions hop, CancellationToken ct)
    {
        var opts = exchange.Value;
        if (!opts.IsConfigured || string.IsNullOrWhiteSpace(hop.Audience))
        {
            logger.LogWarning("Token exchange to {Audience} not configured; outbound call skipped.", hop.Audience);
            return null;
        }

        try
        {
            var token = await cache.GetOrMintAsync(
                TokenCache.ExchangeKey(caller.SubjectToken!, hop.Audience!),
                () => tokens.ExchangeAsync(opts, caller.SubjectToken!, hop.Audience!, ct),
                caller.SubjectExpiresAt, ct);
            return Bearer(token);
        }
        catch (TokenEndpointException ex)
        {
            logger.Log(ex.Kind == TokenErrorKind.InvalidClient ? LogLevel.Error : LogLevel.Warning,
                "Token exchange to {Audience} failed: {Kind} ({StatusCode}) {Description}", hop.Audience, ex.Kind, ex.StatusCode, ex.Description);
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> ServiceAsync(IOutboundHopOptions hop, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(hop.TokenUrl) && !string.IsNullOrWhiteSpace(hop.ClientId) && !string.IsNullOrWhiteSpace(hop.ClientSecret))
        {
            try
            {
                return Bearer(await cache.GetOrMintAsync(TokenCache.ClientCredentialsKey(hop), () => tokens.ClientCredentialsAsync(hop, ct), null, ct));
            }
            catch (TokenEndpointException ex)
            {
                logger.LogWarning("Client-credentials token for {ClientId} failed: {Kind} ({StatusCode}) {Description}",
                    hop.ClientId, ex.Kind, ex.StatusCode, ex.Description);
                return null;
            }
        }

        return string.IsNullOrWhiteSpace(hop.DevUser)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["X-Dev-User"] = hop.DevUser! };
    }

    private static Dictionary<string, string> Bearer(string token) => new() { ["Authorization"] = $"Bearer {token}" };
}
