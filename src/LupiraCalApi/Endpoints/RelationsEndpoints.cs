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

        group.MapGet("/items/{id:guid}/relations", (Guid id, RelationsHandler h, CancellationToken ct) =>
                h.ListForItemAsync(id, ct))
            .WithName("ListItemRelations")
            .WithSummary("List a calendar item's relations.")
            .Produces<List<RelationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/relations", (string toKind, string toRef, RelationsHandler h, CancellationToken ct) =>
                h.FindItemsLinkedToAsync(toKind, toRef, ct))
            .WithName("FindRelatedItems")
            .WithSummary("Reverse lookup: calendar items linked to a given external reference.")
            .Produces<List<CalendarItemDto>>(StatusCodes.Status200OK);

        return app;
    }
}
