using System.Text.Json;

namespace LupiraCalApi.Clients;

/// <summary>Authentik's token endpoint for both outbound grants: RFC 8693 exchange of a member bearer, and
/// client credentials for calls with no member behind them. Singleton over a named client.</summary>
public sealed class TokenEndpointClient(IHttpClientFactory httpFactory)
{
    public const string HttpClientName = "oauth-token";

    private const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    public Task<IssuedToken> ExchangeAsync(TokenExchangeOptions exchange, string subjectToken, string audience, CancellationToken ct) =>
        PostAsync(exchange.TokenUrl!, new Dictionary<string, string>
        {
            ["grant_type"] = TokenExchangeGrant,
            ["client_id"] = exchange.ClientId!,
            ["client_secret"] = exchange.ClientSecret!,
            ["subject_token"] = subjectToken,
            ["subject_token_type"] = AccessTokenType,
            ["audience"] = audience,
            ["scope"] = exchange.Scope,
        }, ct);

    public Task<IssuedToken> ClientCredentialsAsync(IOutboundHopOptions hop, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = hop.ClientId!,
            ["client_secret"] = hop.ClientSecret!,
        };
        if (!string.IsNullOrWhiteSpace(hop.Scope)) form["scope"] = hop.Scope!;
        return PostAsync(hop.TokenUrl!, form, ct);
    }

    private async Task<IssuedToken> PostAsync(string tokenUrl, Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await httpFactory.CreateClient(HttpClientName).PostAsync(tokenUrl, new FormUrlEncodedContent(form), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TokenEndpointException(TokenErrorKind.Unavailable, null, ex.Message);
        }

        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(body).RootElement;
            }
            catch (JsonException)
            {
                throw new TokenEndpointException(
                    resp.IsSuccessStatusCode ? TokenErrorKind.Other : TokenErrorKind.Unavailable, (int) resp.StatusCode, "non-JSON response");
            }

            if (!resp.IsSuccessStatusCode)
                throw new TokenEndpointException(
                    TokenEndpointException.Parse(Text(root, "error")), (int) resp.StatusCode, Text(root, "error_description"));

            var accessToken = Text(root, "access_token");
            if (string.IsNullOrEmpty(accessToken))
                throw new TokenEndpointException(TokenErrorKind.Other, (int) resp.StatusCode, "response had no access_token");
            var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 300;
            return new IssuedToken(accessToken, TimeSpan.FromSeconds(expiresIn));
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
