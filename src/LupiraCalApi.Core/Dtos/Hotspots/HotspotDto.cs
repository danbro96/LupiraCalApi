namespace LupiraCalApi.Core.Dtos.Hotspots;

/// <summary>A place where the caller's events and photos concentrate, derived at read time and ranked by active days.</summary>
public sealed class HotspotDto
{
    /// <summary><c>place:{placeId}</c> when anchored, else <c>cell:{lat},{lon}</c> of its densest ~100 m cell.</summary>
    public required string Id { get; set; }

    public required double Latitude { get; set; }

    public required double Longitude { get; set; }

    /// <summary>Distance from the centre to the farthest contributing point.</summary>
    public required int RadiusM { get; set; }

    /// <summary>The LupiraGeoApi place the hotspot anchors to.</summary>
    public Guid? PlaceId { get; set; }

    /// <summary>The anchor place's name, else a reverse-geocoded label.</summary>
    public string? Label { get; set; }

    /// <summary>Event occurrences here, recurrences expanded.</summary>
    public required int EventCount { get; set; }

    public required int PhotoCount { get; set; }

    /// <summary>Distinct UTC days with an event or a photo here.</summary>
    public required int ActiveDays { get; set; }

    public required DateOnly FirstDay { get; set; }

    public required DateOnly LastDay { get; set; }
}
