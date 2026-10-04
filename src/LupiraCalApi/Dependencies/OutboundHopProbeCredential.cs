using Lupira.Clients.ServiceTokens;
using Lupira.Depz;

namespace LupiraCalApi.Dependencies;

/// <summary>Probes as the real clients authenticate: creds → bearer from the shared <see cref="TokenCache"/>, DevUser → X-Dev-User, else anonymous.</summary>
internal sealed class OutboundHopProbeCredential(IOutboundHopOptions hop, ServiceTokenProvider tokens) : IProbeCredential
{
    public async Task ApplyAsync(HttpRequestMessage request, HttpClient client, CancellationToken ct)
    {
        try
        {
            await tokens.ApplyAsync(request, hop, ct);
        }
        catch (TokenEndpointException ex)
        {
            throw new InvalidOperationException(
                $"token mint failed: {ex.Kind} ({ex.StatusCode?.ToString() ?? "no response"}) {ex.Description}", ex);
        }
    }
}
