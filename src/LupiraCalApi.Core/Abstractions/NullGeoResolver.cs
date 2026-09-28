namespace LupiraCalApi.Core.Abstractions;

/// <summary>Default when LupiraGeoApi isn't configured: resolution falls back to the legacy local catalog.</summary>
public sealed class NullGeoResolver : IGeoResolver
{
    public bool IsConfigured => false;

    public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<GeoPlaceResolution?>(null);

    public Task<IReadOnlyDictionary<Guid, GeoPlaceSummary>?> LookupAsync(IReadOnlyCollection<Guid> placeIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, GeoPlaceSummary>?>(null);

    public Task<GeoPlaceSummary?> NearestAsync(double latitude, double longitude, int radiusM, CancellationToken ct = default) =>
        Task.FromResult<GeoPlaceSummary?>(null);

    public Task<GeoReverseLabel?> ReverseAsync(double latitude, double longitude, CancellationToken ct = default) =>
        Task.FromResult<GeoReverseLabel?>(null);
}
