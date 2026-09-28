using System.Text.Json;
using LupiraCalApi.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Clients;

/// <summary>HTTP <see cref="IPhotoDensitySource"/> against LupiraPhotoApi <c>GET /photos/density</c> as the calling member,
/// authenticated per <see cref="OutboundAuthProvider"/>. A failure returns null so hotspots degrade to events only.</summary>
public sealed class PhotoApiClient(HttpClient http, IOptions<PhotoApiOptions> options, OutboundAuthProvider auth, ILogger<PhotoApiClient> logger) : IPhotoDensitySource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PhotoApiOptions _opts = options.Value;

    public async Task<IReadOnlyList<PhotoDensityCell>?> CellsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        var query = new List<string>(2);
        if (from is { } f) query.Add($"from={Uri.EscapeDataString(f.ToUniversalTime().ToString("O"))}");
        if (to is { } t) query.Add($"to={Uri.EscapeDataString(t.ToUniversalTime().ToString("O"))}");
        var path = query.Count == 0 ? "photos/density" : $"photos/density?{string.Join('&', query)}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            if (!await auth.TryAuthorizeAsync(req, _opts, ct)) return null;

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Photo density returned {Status}.", (int) resp.StatusCode);
                return null;
            }

            var cells = await resp.Content.ReadFromJsonAsync<List<CellItem>>(Json, ct);
            return cells?.Select(c => new PhotoDensityCell(c.Latitude, c.Longitude, c.Count, c.Days)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Photo density failed.");
            return null;
        }
    }

    private sealed class CellItem
    {
        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public int Count { get; set; }

        public List<DateOnly> Days { get; set; } = [];
    }
}
