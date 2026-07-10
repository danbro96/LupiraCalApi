using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LupiraCalApi.Application;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Clients;

/// <summary>HTTP <see cref="IContactResolver"/> against LupiraContactApi <c>POST /internal/contacts/resolve</c>.
/// Service-authed (cached Authentik client-credentials bearer in prod, <c>X-Dev-User</c> locally). A failure
/// returns null so resolution never breaks a write — callers fail open.</summary>
public sealed class ContactApiClient(HttpClient http, IOptions<ContactApiOptions> options, ILogger<ContactApiClient> logger) : IContactResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly ContactApiOptions _opts = options.Value;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<IReadOnlyList<ContactSummary>?> ResolveAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "internal/contacts/resolve")
            {
                Content = JsonContent.Create(new ResolveRequest { ContactIds = [.. contactIds] }, options: Json),
            };
            foreach (var (key, value) in await AuthHeadersAsync(ct))
                req.Headers.TryAddWithoutValidation(key, value);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Contact resolve returned {Status} for {Count} ids.", (int)resp.StatusCode, contactIds.Count);
                return null;
            }
            var body = await resp.Content.ReadFromJsonAsync<ResolveResponse>(Json, ct);
            return body is null ? null : [.. body.Contacts.Select(c => new ContactSummary(c.ContactId, c.DisplayName))];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Contact resolve failed for {Count} ids.", contactIds.Count);
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
            // Requesting the scope is what pulls in the audience mapping (aud=lupira-contact); binding it on the
            // provider alone is not enough — contact-api rejects a token without that aud.
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

    private sealed class ResolveRequest { public required List<Guid> ContactIds { get; set; } }

    private sealed class ResolveResponse
    {
        public List<ResolvedContact> Contacts { get; set; } = [];
    }

    private sealed class ResolvedContact
    {
        public Guid ContactId { get; set; }
        public string DisplayName { get; set; } = "";
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
    }
}
