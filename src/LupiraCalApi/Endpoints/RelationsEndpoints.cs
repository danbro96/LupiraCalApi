using LupiraCalApi.Dtos.Events;
using LupiraCalApi.Dtos.Relations;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class RelationsEndpoints
{
    public static IEndpointRouteBuilder MapRelations(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("ApiPolicy").WithTags("Relations");

        group.MapPost("/events/{id:guid}/relations", (Guid id, CreateRelationRequest body, RelationsHandler h, CancellationToken ct) =>
                h.LinkEventAsync(id, body, ct))
            .WithSummary("Link an event to an external reference (e.g. a LupiraTasks item).")
            .Produces<RelationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/events/{id:guid}/relations", (Guid id, RelationsHandler h, CancellationToken ct) =>
                h.ListForEventAsync(id, ct))
            .WithSummary("List an event's relations.")
            .Produces<List<RelationDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/relations", (string toKind, string toRef, RelationsHandler h, CancellationToken ct) =>
                h.FindEventsLinkedToAsync(toKind, toRef, ct))
            .WithSummary("Reverse lookup: events linked to a given external reference (e.g. a task).")
            .Produces<List<EventDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
