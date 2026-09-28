namespace LupiraCalApi.Core.Application.Hotspots;

/// <summary>One weighted input to <see cref="HotspotClusterer"/>: an event place with its occurrences, or a photo cell.</summary>
internal sealed class DensityPoint
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public Guid? PlaceId { get; init; }

    public int EventCount { get; init; }

    public int PhotoCount { get; init; }

    public required IReadOnlySet<DateOnly> Days { get; init; }
}
