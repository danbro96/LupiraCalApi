using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class CalendarsEndpoints
{
    public static IEndpointRouteBuilder MapCalendars(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/calendars").RequireAuthorization("ApiPolicy").WithTags("Calendars");

        group.MapGet("/", (CalendarsHandler h, CancellationToken ct) => h.ListAsync(ct))
            .WithName("ListContainers")
            .WithSummary("List the calendars the caller can access.")
            .Produces<List<ContainerDto>>(StatusCodes.Status200OK);

        group.MapPost("/", (CreateCalendarRequest body, CalendarsHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithName("CreateCalendar")
            .WithSummary("Create a calendar. DefaultTimezone (IANA id) defaults to the server default. (Address books are managed by LupiraContactApi.)")
            .Produces<ContainerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{calendarId:guid}", (Guid calendarId, UpdateCalendarRequest body, CalendarsHandler h, CancellationToken ct) => h.UpdateAsync(calendarId, body, ct))
            .WithName("UpdateCalendar")
            .WithSummary("Change a calendar's DefaultTimezone (IANA id); owner-only. Applies to items written later without a zone; existing items keep theirs.")
            .Produces<ContainerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
