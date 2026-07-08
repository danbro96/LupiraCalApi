using LupiraCalApi.Application;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Places;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class PlacesHandler(PlaceService places)
{
    public async Task<Results<Ok<PlaceDto>, NotFound, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct) =>
        OpResultMap.OkNotFound(await places.GetAsync(id, ct));

    public async Task<Results<Ok<List<PlaceDto>>, UnauthorizedHttpResult>> GetManyAsync(Guid[] ids, CancellationToken ct) =>
        OpResultMap.OkOnly(await places.GetManyAsync(ids, ct));

    public async Task<Results<Ok<List<PlaceDto>>, UnauthorizedHttpResult>> SearchAsync(string? search, PlaceKind? kind, Guid? parentPlaceId, CancellationToken ct) =>
        OpResultMap.OkOnly(await places.SearchAsync(search, kind, parentPlaceId, ct));
}
