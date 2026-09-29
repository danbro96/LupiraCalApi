using System.Globalization;
using System.Net;
using System.Text.Json;
using LupiraCalApi.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Clients;

/// <summary>HTTP <see cref="IGeoResolver"/> against LupiraGeoApi (<c>POST /places/resolve</c>, <c>POST /places/lookup</c>,
/// <c>GET /places</c> by proximity, <c>GET /geocode/reverse</c>), authenticated per <see cref="OutboundAuthProvider"/>.
/// A failure returns null so a resolve never breaks an item write — the caller then keeps the existing place id or
/// stores the raw label.</summary>
public sealed class GeoApiClient(HttpClient http, IOptions<GeoApiOptions> options, OutboundAuthProvider auth, ILogger<GeoApiClient> logger) : IGeoResolver
{
    private const int LookupBatch = 200;

    // Geo's PlaceKind for a whole settlement/administrative area, as serialized on the wire.
    private const string AreaKind = "Area";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly GeoApiOptions _opts = options.Value;

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default)
    {
        var body = await SendAsync<ResolveResponse>(() => new HttpRequestMessage(HttpMethod.Post, "places/resolve")
        {
            Content = JsonContent.Create(new ResolveRequest { Text = text }, options: Json),
        }, $"resolve for '{text}'", ct);
        // PlaceId is null on GeocodeUnavailable (geocoder unreachable) — a retryable no-resolution, not a place.
        return body?.PlaceId is { } pid && pid != Guid.Empty
            ? new GeoPlaceResolution(pid, body.Name, body.Latitude, body.Longitude) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, GeoPlaceSummary>?> LookupAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default)
    {
        var found = new Dictionary<Guid, GeoPlaceSummary>(placeIds.Count);
        foreach (var chunk in placeIds.Distinct().Chunk(LookupBatch))
        {
            var items = await SendAsync<List<LookupItem>>(() => new HttpRequestMessage(HttpMethod.Post, "places/lookup")
            {
                Content = JsonContent.Create(new LookupRequest { Ids = [.. chunk] }, options: Json),
            }, $"lookup of {chunk.Length} ids", ct);
            if (items is null) return null;
            foreach (var item in items)
                if (item.Place is { } p) found[item.RequestedId] = p.ToSummary();
        }

        return found;
    }

    public async Task<GeoPlaceSummary?> NearestAsync(double latitude, double longitude, int radiusM, CancellationToken ct = default)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"places?nearLat={latitude}&nearLon={longitude}&radiusM={radiusM}&limit=1");
        var places = await SendAsync<List<PlaceItem>>(() => new HttpRequestMessage(HttpMethod.Get, path), "nearest place", ct);
        return places?.FirstOrDefault()?.ToSummary();
    }

    public async Task<GeoReverseLabel?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"geocode/reverse?lat={latitude}&lon={longitude}");
        var result = await SendAsync<ReverseResponse>(() => new HttpRequestMessage(HttpMethod.Get, path), "reverse geocode", ct);
        return result is null || string.IsNullOrWhiteSpace(result.DisplayName) ? null : new GeoReverseLabel(result.DisplayName, result.Locality);
    }

    private async Task<T?> SendAsync<T>(Func<HttpRequestMessage> build, string what, CancellationToken ct) where T : class
    {
        try
        {
            using var req = build();
            if (!await auth.TryAuthorizeAsync(req, _opts, ct)) return null;

            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Geo {What} returned {Status}.", what, (int) resp.StatusCode);
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Geo {What} failed.", what);
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

    private sealed class LookupRequest
    {
        public required List<Guid> Ids { get; set; }
    }

    private sealed class LookupItem
    {
        public Guid RequestedId { get; set; }

        public PlaceItem? Place { get; set; }
    }

    private sealed class PlaceItem
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public string? Kind { get; set; }

        public GeoPlaceSummary ToSummary() => new(Id, Name, Latitude, Longitude, string.Equals(Kind, AreaKind, StringComparison.Ordinal));
    }

    private sealed class ReverseResponse
    {
        public string DisplayName { get; set; } = string.Empty;

        public string? Locality { get; set; }
    }
}
