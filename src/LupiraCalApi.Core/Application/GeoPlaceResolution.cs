namespace LupiraCalApi.Core.Application;

/// <summary>The authoritative resolution of a free-text location by LupiraGeoApi: a stable place id + canonical name and
/// (when known) coordinates.</summary>
public sealed record GeoPlaceResolution(Guid PlaceId, string Name, double? Latitude, double? Longitude);
