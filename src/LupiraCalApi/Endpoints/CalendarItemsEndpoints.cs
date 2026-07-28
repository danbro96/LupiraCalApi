using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Handlers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Endpoints;

public static class CalendarItemsEndpoints
{
    public static IEndpointRouteBuilder MapCalendarItems(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items").RequireAuthorization("ApiPolicy").WithTags("CalendarItems");

        group.MapGet("/", (string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId,
                string? tag, Guid? parentId, Guid? contactId, string? category, string? status, int? skip, int? take,
                CalendarItemsHandler h, CancellationToken ct, bool desc = false) =>
                h.SearchAsync(query, from, to, calendarId, tag, parentId, contactId, category, status, skip, take, desc, ct))
            .WithName("SearchItems")
            .WithSummary("Search calendar items (text + tag + parent + attendee contact + category/status filter; recurrence expanded in-window). Text queries and parent/contact filters with no from/to match all-time; otherwise the window defaults to ±1 year. skip/take page over occurrences sorted by start (desc=true for newest first). Only items accepted into a calendar you can read.")
            .Produces<List<CalendarItemOccurrenceDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateCalendarItemRequest body, CalendarItemsHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithName("CreateItem")
            .WithSummary("Create a calendar item (filed into CalendarId if given, else unfiled for later curation). A location must be a resolved PlaceId (free text is CalDAV-only).")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/batch", (CreateCalendarItemsBatchRequest body, CalendarItemsHandler h, CancellationToken ct) => h.CreateBatchAsync(body, ct))
            .WithName("CreateItemsBatch")
            .WithSummary("Create many items in one call (idempotent per item on SourceKey; children reference parents by ParentSourceKey in any order). Returns a per-item result (created|existed|invalid) in input order.")
            .Produces<List<ItemBatchResult>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, CalendarItemsHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetItem")
            .WithSummary("Get a single calendar item.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/thin", (Guid? calendarId, string? category, double? maxScore, int? take, CalendarItemsHandler h, CancellationToken ct) =>
                h.ThinAsync(calendarId, category, maxScore, take, ct))
            .WithName("GetThinItems")
            .WithSummary("Check-in worklist: items ranked thinnest-first by completeness score (< maxScore, default 1 = any item with gaps). Item-granular (no recurrence expansion); exempt items (system/Birthdays/Availability calendars, cancelled) are excluded. Acknowledge an inapplicable gap field by merging metadata {\"completeness\":{\"na\":[\"booking\"]}} so it stops counting.")
            .Produces<List<CalendarItemDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/by-place/{placeId:guid}", (Guid placeId, CalendarItemsHandler h, CancellationToken ct) => h.ByPlaceAsync(placeId, ct))
            .WithName("GetItemsByPlace")
            .WithSummary("Calendar items anchored to a LupiraGeoApi place (its location, or a travel endpoint). Only items in a calendar you can read.")
            .Produces<List<CalendarItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", (Guid id, UpdateCalendarItemRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CalendarItemsHandler h, CancellationToken ct) => h.UpdateAsync(id, body, idempotencyKey, ct))
            .WithName("UpdateItem")
            .WithSummary("Update a calendar item. Plain fields: omitted = kept; fields paired with a *Provided sentinel are written verbatim when it is true (enables clearing recurrence, switching all-day, editing timezones). Offline clients send Idempotency-Key (their command id) + body OccurredAt for replay-safe, last-writer-wins updates.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", (Guid id, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CalendarItemsHandler h, CancellationToken ct) => h.DeleteAsync(id, idempotencyKey, ct))
            .WithName("DeleteItem")
            .WithSummary("Delete a calendar item (soft delete + tombstone). A replay bearing the same Idempotency-Key succeeds instead of 404ing.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/metadata", (Guid id, JsonNode patch, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CalendarItemsHandler h, CancellationToken ct) => h.AttachMetadataAsync(id, patch, occurredAt, idempotencyKey, ct))
            .WithName("MergeItemMetadata")
            .WithSummary("Merge arbitrary JSON metadata into a calendar item. Offline clients pass ?occurredAt= + Idempotency-Key for replay-safe, last-writer-wins merges.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}/prompt", (Guid id, SetItemPromptRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CalendarItemsHandler h, CancellationToken ct) => h.SetPromptAsync(id, body, idempotencyKey, ct))
            .WithName("SetItemPrompt")
            .WithSummary("Set the LLM-interpreted payload on an item (server-side only; never in the export). 409 if the item carries an action.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}/prompt", (Guid id, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CalendarItemsHandler h, CancellationToken ct) => h.ClearPromptAsync(id, occurredAt, idempotencyKey, ct))
            .WithName("ClearItemPrompt")
            .WithSummary("Clear the item's LLM payload.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}/action", (Guid id, SetItemActionRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CalendarItemsHandler h, CancellationToken ct) => h.SetActionAsync(id, body, idempotencyKey, ct))
            .WithName("SetItemAction")
            .WithSummary("Set the deterministic payload on an item (server-side only; never in the export). 409 if the item carries a prompt.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}/action", (Guid id, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CalendarItemsHandler h, CancellationToken ct) => h.ClearActionAsync(id, occurredAt, idempotencyKey, ct))
            .WithName("ClearItemAction")
            .WithSummary("Clear the item's deterministic payload.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
