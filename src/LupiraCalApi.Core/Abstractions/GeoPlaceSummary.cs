namespace LupiraCalApi.Core.Abstractions;

/// <summary>A live LupiraGeoApi place: id, canonical name and (when located) coordinates.</summary>
public sealed record GeoPlaceSummary(Guid PlaceId, string Name, double? Latitude, double? Longitude);
