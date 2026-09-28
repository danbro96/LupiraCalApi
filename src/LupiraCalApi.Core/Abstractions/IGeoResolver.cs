namespace LupiraCalApi.Core.Abstractions;

/// <summary>
/// Resolves free-text locations to a shared <c>LupiraGeoApi</c> place. The gazetteer is authoritative there; this cal
/// service keeps only a local mirror (keyed by the geo place id) so ICS generation and the place→items reverse index stay
/// local. Implemented over HTTP by the host; a no-op default (<see cref="NullGeoResolver"/>, <c>IsConfigured=false</c>)
/// keeps the legacy local-catalog behaviour when geo isn't wired (dev/test).
/// </summary>
public interface IGeoResolver
{
    bool IsConfigured { get; }

    Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default);

    /// <summary>Places by id, keyed by the requested id (merged ids map to the survivor). <c>null</c> = geo unavailable;
    /// absent keys in a non-null result are unknown or deleted.</summary>
    Task<IReadOnlyDictionary<Guid, GeoPlaceSummary>?> LookupAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default);

    Task<GeoPlaceSummary?> NearestAsync(double latitude, double longitude, int radiusM, CancellationToken ct = default);

    Task<GeoReverseLabel?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default);
}
