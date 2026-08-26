namespace LupiraCalApi.Core.Dtos.Internal;

public sealed class ItemPlaceRefDto
{
    public required Guid PlaceId { get; set; }

    public required int LiveCount { get; set; }

    public required int DeletedCount { get; set; }
}
