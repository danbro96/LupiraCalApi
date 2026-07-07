using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Worker.Clients;

/// <summary>
/// Resolves the service-auth headers for the assistant hop: a cached Authentik client-credentials bearer in prod
/// (refreshed before expiry — the dispatcher ticks every 15 s, so per-call fetches would hammer the IdP), or the
/// <c>X-Dev-Service</c> header in dev.
/// </summary>
public sealed class ServiceTokenProvider(IHttpClientFactory httpFactory, IOptions<AssistantOptions> options)
{
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly AssistantOptions _opts = options.Value;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyDictionary<string, string>> ResolveAuthHeadersAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_opts.TokenUrl) && !string.IsNullOrWhiteSpace(_opts.ClientId) && !string.IsNullOrWhiteSpace(_opts.ClientSecret))
            return new Dictionary<string, string> { ["Authorization"] = $"Bearer {await GetTokenAsync(ct)}" };

        if (!string.IsNullOrWhiteSpace(_opts.DevServiceId))
            return new Dictionary<string, string> { ["X-Dev-Service"] = _opts.DevServiceId! };

        return new Dictionary<string, string>();
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is { } cached && DateTimeOffset.UtcNow < _expiresAt) return cached;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_token is { } fresh && DateTimeOffset.UtcNow < _expiresAt) return fresh;

            using var http = httpFactory.CreateClient(nameof(ServiceTokenProvider));
            using var resp = await http.PostAsync(_opts.TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _opts.ClientId!,
                ["client_secret"] = _opts.ClientSecret!,
            }), ct);
            resp.EnsureSuccessStatusCode();
            var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("Client-credentials token response was empty.");
            _token = token.AccessToken ?? throw new InvalidOperationException("Client-credentials token response had no access_token.");
            _expiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(token.ExpiresIn ?? 300) - ExpirySkew;
            return _token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }
}
