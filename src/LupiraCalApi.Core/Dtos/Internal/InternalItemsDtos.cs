namespace LupiraCalApi.Core.Dtos.Internal;

public sealed class CheckPlaceReferencesRequest
{
    public required List<Guid> PlaceIds { get; set; }
}

public sealed class ItemPlaceRefDto
{
    public required Guid PlaceId { get; set; }
    public required int LiveCount { get; set; }
    public required int DeletedCount { get; set; }
}

public sealed class ItemPlaceReferencesResponse
{
    public required List<ItemPlaceRefDto> Places { get; set; }
}
