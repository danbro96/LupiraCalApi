using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Relations;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class RelationsEndpoints
{
    public static IEndpointRouteBuilder MapRelations(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty).RequireAuthorization("ApiPolicy").WithTags("Relations");

        group.MapPost("/items/{id:guid}/relations", (Guid id, CreateRelationRequest body, RelationsHandler h, CancellationToken ct) =>
                h.LinkItemAsync(id, body, ct))
            .WithName("CreateItemRelation")
            .WithSummary("Link a calendar item to an external reference (e.g. a LupiraTasks item, or an Activity-API engagement/project).")
            .Produces<RelationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/items/{id:guid}/relations/batch", (Guid id, CreateRelationsBatchRequest body, RelationsHandler h, CancellationToken ct) =>
                h.LinkItemBatchAsync(id, body, ct))
            .WithName("CreateItemRelationsBatch")
            .WithSummary("Link many references of one kind to an item at once (idempotent per reference) — e.g. an album's photos to its event.")
            .Produces<List<RelationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/items/{id:guid}/relations", (Guid id, RelationsHandler h, CancellationToken ct) =>
                h.ListForItemAsync(id, ct))
            .WithName("ListItemRelations")
            .WithSummary("List a calendar item's relations.")
            .Produces<List<RelationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/relations/edges", (string toKind, RelationsHandler h, CancellationToken ct) =>
                h.ListEdgesByKindAsync(toKind, ct))
            .WithName("ListRelationEdges")
            .WithSummary("Every edge of one kind the caller can see, with its item and reference — e.g. all photo links.")
            .Produces<List<RelationDto>>(StatusCodes.Status200OK);

        group.MapGet("/relations", (string toKind, string toRef, RelationsHandler h, CancellationToken ct) =>
                h.FindItemsLinkedToAsync(toKind, toRef, ct))
            .WithName("FindRelatedItems")
            .WithSummary("Reverse lookup: calendar items linked to a given external reference.")
            .Produces<List<CalendarItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
