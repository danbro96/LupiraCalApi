namespace LupiraCalApi.Core.Dtos.Internal;

public sealed class CheckPlaceReferencesRequest
{
    public required List<Guid> PlaceIds { get; set; }
}
