namespace LupiraCalApi.Core.Abstractions;

/// <summary>A live LupiraGeoApi place: id, canonical name, (when located) coordinates, and whether geo classifies it as a
/// whole settlement/administrative area — a city centroid rather than a venue.</summary>
public sealed record GeoPlaceSummary(Guid PlaceId, string Name, double? Latitude, double? Longitude, bool IsArea = false);
