using LupiraCalApi.Dtos.Events;
using LupiraCalApi.Handlers;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Endpoints;

public static class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEvents(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events").RequireAuthorization("ApiPolicy").WithTags("Events");

        group.MapGet("/", (string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId,
                string? tag, string? metadata, EventsHandler h, CancellationToken ct) =>
                h.SearchAsync(query, from, to, calendarId, tag, metadata, ct))
            .WithSummary("Search events (full-text + fuzzy; tag + JSONB metadata filters; recurrence expanded in-window).")
            .Produces<List<EventOccurrenceDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateEventRequest body, EventsHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithSummary("Create an event.")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, EventsHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithSummary("Get a single event.")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", (Guid id, UpdateEventRequest body, EventsHandler h, CancellationToken ct) => h.UpdateAsync(id, body, ct))
            .WithSummary("Update an event (only provided fields change).")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", (Guid id, EventsHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithSummary("Delete an event (soft delete + tombstone).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/metadata", (Guid id, JsonNode patch, EventsHandler h, CancellationToken ct) => h.AttachMetadataAsync(id, patch, ct))
            .WithSummary("Merge arbitrary JSON metadata into an event.")
            .Produces<EventDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
