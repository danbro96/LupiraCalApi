using LupiraCalApi.Domain;
using System.Text.Json.Serialization;

namespace LupiraCalApi.Dtos.Places;

/// <summary>A node in the shared location catalog; <c>ParentPlaceId</c> walks up the hierarchy (Address → City → Country).</summary>
public sealed class PlaceDto
{
    public required Guid Id { get; set; }
    public Guid? ParentPlaceId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PlaceKind>))]
    public required PlaceKind Kind { get; set; }

    public required string Name { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
