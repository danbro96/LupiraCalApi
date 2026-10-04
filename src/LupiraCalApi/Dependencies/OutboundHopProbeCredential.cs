using Lupira.Depz;
using LupiraCalApi.Clients;

namespace LupiraCalApi.Dependencies;

/// <summary>Probes as the real clients authenticate: creds → bearer from the shared <see cref="TokenCache"/>, DevUser → X-Dev-User, else anonymous.</summary>
internal sealed class OutboundHopProbeCredential(IOutboundHopOptions hop, TokenEndpointClient tokens, TokenCache cache) : IProbeCredential
{
    public async Task ApplyAsync(HttpRequestMessage request, HttpClient client, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(hop.TokenUrl) && !string.IsNullOrWhiteSpace(hop.ClientId)
            && !string.IsNullOrWhiteSpace(hop.ClientSecret))
        {
            try
            {
                var token = await cache.GetOrMintAsync(TokenCache.ClientCredentialsKey(hop), token => tokens.ClientCredentialsAsync(hop, token), null, ct);
                request.Headers.Authorization = new("Bearer", token);
            }
            catch (TokenEndpointException ex)
            {
                throw new InvalidOperationException(
                    $"token mint failed: {ex.Kind} ({ex.StatusCode?.ToString() ?? "no response"}) {ex.Description}", ex);
            }
        }
        else if (!string.IsNullOrWhiteSpace(hop.DevUser))
        {
            request.Headers.TryAddWithoutValidation("X-Dev-User", hop.DevUser);
        }
    }
}
