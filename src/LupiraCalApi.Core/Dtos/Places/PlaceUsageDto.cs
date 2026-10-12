namespace LupiraCalApi.Core.Dtos.Places;

/// <summary>How the caller's events have used one place: as their location or a travel endpoint.</summary>
public sealed class PlaceUsageDto
{
    /// <summary>The LupiraGeoApi place; a merged-away id reports its survivor.</summary>
    public required Guid PlaceId { get; set; }

    public string? Label { get; set; }

    /// <summary>Event occurrences here, recurrences expanded.</summary>
    public required int EventCount { get; set; }

    public required DateOnly LastDay { get; set; }

    public required List<PlaceCategoryUsageDto> ByCategory { get; set; }

    public required List<PlaceCompanionDto> Companions { get; set; }
}
