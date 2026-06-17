using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Relations;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class RelationsEndpoints
{
    public static IEndpointRouteBuilder MapRelations(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("ApiPolicy").WithTags("Relations");

        group.MapPost("/items/{id:guid}/relations", (Guid id, CreateRelationRequest body, RelationsHandler h, CancellationToken ct) =>
                h.LinkItemAsync(id, body, ct))
            .WithSummary("Link a calendar item to an external reference (e.g. a LupiraTasks item, or an Activity-API engagement/project).")
            .Produces<RelationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/items/{id:guid}/relations", (Guid id, RelationsHandler h, CancellationToken ct) =>
                h.ListForItemAsync(id, ct))
            .WithSummary("List a calendar item's relations.")
            .Produces<List<RelationDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/relations", (string toKind, string toRef, RelationsHandler h, CancellationToken ct) =>
                h.FindItemsLinkedToAsync(toKind, toRef, ct))
            .WithSummary("Reverse lookup: calendar items linked to a given external reference.")
            .Produces<List<CalendarItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
