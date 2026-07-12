using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraCalApi.Application;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Clients;

/// <summary>HTTP <see cref="IGeoResolver"/> against LupiraGeoApi <c>POST /places/resolve</c>. Service-authed (cached
/// Authentik client-credentials bearer in prod, <c>X-Dev-User</c> locally). A failure returns null so a resolve never
/// breaks an item write — the caller then keeps the existing place id or provisions locally.</summary>
public sealed class GeoApiClient(HttpClient http, IOptions<GeoApiOptions> options, ILogger<GeoApiClient> logger) : IGeoResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly GeoApiOptions _opts = options.Value;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "places/resolve")
            {
                Content = JsonContent.Create(new ResolveRequest { Text = text }, options: Json),
            };
            foreach (var (key, value) in await AuthHeadersAsync(ct))
                req.Headers.TryAddWithoutValidation(key, value);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Geo resolve returned {Status} for '{Text}'.", (int)resp.StatusCode, text);
                return null;
            }
            var body = await resp.Content.ReadFromJsonAsync<ResolveResponse>(Json, ct);
            // PlaceId is null on GeocodeUnavailable (geocoder unreachable) — a retryable no-resolution, not a place.
            return body?.PlaceId is { } pid && pid != Guid.Empty
                ? new GeoPlaceResolution(pid, body.Name, body.Latitude, body.Longitude) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Geo resolve failed for '{Text}'.", text);
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> AuthHeadersAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_opts.TokenUrl) && !string.IsNullOrWhiteSpace(_opts.ClientId) && !string.IsNullOrWhiteSpace(_opts.ClientSecret))
            return new Dictionary<string, string> { ["Authorization"] = $"Bearer {await GetTokenAsync(ct)}" };
        if (!string.IsNullOrWhiteSpace(_opts.DevUser))
            return new Dictionary<string, string> { ["X-Dev-User"] = _opts.DevUser! };
        return new Dictionary<string, string>();
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is { } cached && DateTimeOffset.UtcNow < _expiresAt) return cached;
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_token is { } fresh && DateTimeOffset.UtcNow < _expiresAt) return fresh;
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _opts.ClientId!,
                ["client_secret"] = _opts.ClientSecret!,
            };
            // Requesting the scope is what pulls in the audience mapping (aud=lupira-geo); binding it on the provider
            // alone is not enough. Geo rejects a token without that aud.
            if (!string.IsNullOrWhiteSpace(_opts.Scope)) form["scope"] = _opts.Scope!;
            using var resp = await http.PostAsync(_opts.TokenUrl, new FormUrlEncodedContent(form), ct);
            resp.EnsureSuccessStatusCode();
            var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException("Client-credentials token response was empty.");
            _token = token.AccessToken ?? throw new InvalidOperationException("Token response had no access_token.");
            _expiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(token.ExpiresIn ?? 300) - ExpirySkew;
            return _token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private sealed class ResolveRequest { public required string Text { get; set; } }

    private sealed class ResolveResponse
    {
        public Guid? PlaceId { get; set; }
        public string Name { get; set; } = "";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
    }
}
