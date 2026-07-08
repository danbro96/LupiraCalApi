using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Places;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class PlacesEndpoints
{
    public static IEndpointRouteBuilder MapPlaces(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/places").RequireAuthorization("ApiPolicy").WithTags("Places");

        // The collection: batch-resolve by ids=, or browse/search by name/kind/parent. ids wins when present.
        group.MapGet("/", (Guid[]? ids, string? search, PlaceKind? kind, Guid? parentPlaceId, PlacesHandler h, CancellationToken ct) =>
                ids is { Length: > 0 } ? h.GetManyAsync(ids, ct) : h.SearchAsync(search, kind, parentPlaceId, ct))
            .WithName("GetPlaces")
            .WithSummary("Browse/search the shared location catalog: filter by name (search), kind, or parentPlaceId; or batch-resolve by repeating ids= (ids wins; unknown ids omitted).")
            .Produces<List<PlaceDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, PlacesHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetPlace")
            .WithSummary("A single place from the shared location catalog.")
            .Produces<PlaceDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}/items", (Guid id, CalendarItemsHandler h, CancellationToken ct) => h.ByPlaceAsync(id, ct))
            .WithName("GetPlaceItems")
            .WithSummary("Calendar items anchored to this place (its location, or a travel/car endpoint). Only items in a calendar you can read.")
            .Produces<List<CalendarItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
