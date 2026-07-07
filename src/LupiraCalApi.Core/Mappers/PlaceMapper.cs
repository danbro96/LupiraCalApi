using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Places;

namespace LupiraCalApi.Mappers;

internal static class PlaceMapper
{
    public static PlaceDto ToResponse(this Place p) => new()
    {
        Id = p.Id,
        ParentPlaceId = p.ParentPlaceId,
        Kind = p.Kind,
        Name = p.Name,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
    };
}
