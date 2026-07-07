using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class CurationEndpoints
{
    public static IEndpointRouteBuilder MapCuration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").RequireAuthorization("ApiPolicy").WithTags("Curation");

        group.MapGet("/calendars/{calendarId:guid}/proposed", (Guid calendarId, CurationHandler h, CancellationToken ct) => h.ListProposedAsync(calendarId, ct))
            .WithName("ListProposedItems")
            .WithSummary("List items proposed into a calendar (awaiting accept/reject).")
            .Produces<List<CalendarItemDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/items/{itemId:guid}/calendars/{calendarId:guid}/accept", (Guid itemId, Guid calendarId, CurationHandler h, CancellationToken ct) => h.AcceptAsync(itemId, calendarId, ct))
            .WithName("AcceptItemIntoCalendar")
            .WithSummary("Accept a proposed item into a calendar (then visible over DAV).")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/items/{itemId:guid}/calendars/{calendarId:guid}", (Guid itemId, Guid calendarId, string? status, CurationHandler h, CancellationToken ct) => h.AddToCalendarAsync(itemId, calendarId, status, ct))
            .WithName("FileItemToCalendar")
            .WithSummary("File an existing item into a calendar (status=proposed|accepted, default proposed).")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/items/{itemId:guid}/calendars/{calendarId:guid}", (Guid itemId, Guid calendarId, CurationHandler h, CancellationToken ct) => h.RejectAsync(itemId, calendarId, ct))
            .WithName("RemoveItemFromCalendar")
            .WithSummary("Remove an item from a calendar (reject / unfile).")
            .Produces<CalendarItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
