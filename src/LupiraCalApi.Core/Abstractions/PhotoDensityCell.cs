namespace LupiraCalApi.Core.Abstractions;

/// <summary>One ~100 m cell of the caller's photos: its centre, photo count and the distinct UTC days they were taken.</summary>
public sealed record PhotoDensityCell(double Latitude, double Longitude, int Count, IReadOnlyList<DateOnly> Days);
