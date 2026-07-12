using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Handlers;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Endpoints;

public static class CalendarItemsEndpoints
{
    public static IEndpointRouteBuilder MapCalendarItems(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items").RequireAuthorization("ApiPolicy").WithTags("CalendarItems");

        group.MapGet("/", (string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId,
                string? tag, Guid? parentId, CalendarItemsHandler h, CancellationToken ct) =>
                h.SearchAsync(query, from, to, calendarId, tag, parentId, ct))
            .WithName("SearchItems")
            .WithSummary("Search calendar items (text + tag + parent filter; recurrence expanded in-window). Only items accepted into a calendar you can read.")
            .Produces<List<CalendarItemOccurrenceDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateCalendarItemRequest body, CalendarItemsHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithName("CreateItem")
            .WithSummary("Create a calendar item (filed into CalendarId if given, else unfiled for later curation).")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, CalendarItemsHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetItem")
            .WithSummary("Get a single calendar item.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/by-place/{placeId:guid}", (Guid placeId, CalendarItemsHandler h, CancellationToken ct) => h.ByPlaceAsync(placeId, ct))
            .WithName("GetItemsByPlace")
            .WithSummary("Calendar items anchored to a LupiraGeoApi place (its location, or a travel endpoint). Only items in a calendar you can read.")
            .Produces<List<CalendarItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", (Guid id, UpdateCalendarItemRequest body, CalendarItemsHandler h, CancellationToken ct) => h.UpdateAsync(id, body, ct))
            .WithName("UpdateItem")
            .WithSummary("Update a calendar item (only provided fields change).")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", (Guid id, CalendarItemsHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithName("DeleteItem")
            .WithSummary("Delete a calendar item (soft delete + tombstone).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/metadata", (Guid id, JsonNode patch, CalendarItemsHandler h, CancellationToken ct) => h.AttachMetadataAsync(id, patch, ct))
            .WithName("MergeItemMetadata")
            .WithSummary("Merge arbitrary JSON metadata into a calendar item.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}/prompt", (Guid id, SetItemPromptRequest body, CalendarItemsHandler h, CancellationToken ct) => h.SetPromptAsync(id, body, ct))
            .WithName("SetItemPrompt")
            .WithSummary("Set the LLM-interpreted payload on an item (server-side only; never in ICS). 409 if the item carries an action.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}/prompt", (Guid id, CalendarItemsHandler h, CancellationToken ct) => h.ClearPromptAsync(id, ct))
            .WithName("ClearItemPrompt")
            .WithSummary("Clear the item's LLM payload.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}/action", (Guid id, SetItemActionRequest body, CalendarItemsHandler h, CancellationToken ct) => h.SetActionAsync(id, body, ct))
            .WithName("SetItemAction")
            .WithSummary("Set the deterministic payload on an item (server-side only; never in ICS). 409 if the item carries a prompt.")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}/action", (Guid id, CalendarItemsHandler h, CancellationToken ct) => h.ClearActionAsync(id, ct))
            .WithName("ClearItemAction")
            .WithSummary("Clear the item's deterministic payload.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
