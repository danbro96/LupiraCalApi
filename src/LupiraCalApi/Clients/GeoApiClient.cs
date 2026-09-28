using System.Text.Json;
using LupiraCalApi.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Clients;

/// <summary>HTTP <see cref="IGeoResolver"/> against LupiraGeoApi <c>POST /places/resolve</c>, authenticated per
/// <see cref="OutboundAuthProvider"/>. A failure returns null so a resolve never breaks an item write — the caller then
/// keeps the existing place id or stores the raw label.</summary>
public sealed class GeoApiClient(HttpClient http, IOptions<GeoApiOptions> options, OutboundAuthProvider auth, ILogger<GeoApiClient> logger) : IGeoResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly GeoApiOptions _opts = options.Value;

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "places/resolve")
            {
                Content = JsonContent.Create(new ResolveRequest { Text = text }, options: Json),
            };
            if (!await auth.TryAuthorizeAsync(req, _opts, ct)) return null;

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Geo resolve returned {Status} for '{Text}'.", (int) resp.StatusCode, text);
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

    private sealed class ResolveRequest
    {
        public required string Text { get; set; }
    }

    private sealed class ResolveResponse
    {
        public Guid? PlaceId { get; set; }

        public string Name { get; set; } = string.Empty;

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }
}
