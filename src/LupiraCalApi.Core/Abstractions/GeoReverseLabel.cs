namespace LupiraCalApi.Core.Abstractions;

/// <summary>A reverse-geocoded coordinate: the geocoder's full display name and, when known, its locality.</summary>
public sealed record GeoReverseLabel(string DisplayName, string? Locality);
