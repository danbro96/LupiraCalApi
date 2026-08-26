namespace LupiraCalApi.Core.Abstractions;

/// <summary>Default when LupiraGeoApi isn't configured: resolution falls back to the legacy local catalog.</summary>
public sealed class NullGeoResolver : IGeoResolver
{
    public bool IsConfigured => false;
    public Task<GeoPlaceResolution?> ResolveAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<GeoPlaceResolution?>(null);
}
