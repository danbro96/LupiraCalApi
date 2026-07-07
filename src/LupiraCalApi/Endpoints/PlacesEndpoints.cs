using LupiraCalApi.Dtos.Places;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class PlacesEndpoints
{
    public static IEndpointRouteBuilder MapPlaces(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/places").RequireAuthorization("ApiPolicy").WithTags("Places");

        group.MapGet("/", (Guid[] ids, PlacesHandler h, CancellationToken ct) => h.GetManyAsync(ids, ct))
            .WithSummary("Batch-resolve places by id (repeat ids=; unknown ids are omitted).")
            .Produces<List<PlaceDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, PlacesHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithSummary("A single place from the shared location catalog.")
            .Produces<PlaceDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
