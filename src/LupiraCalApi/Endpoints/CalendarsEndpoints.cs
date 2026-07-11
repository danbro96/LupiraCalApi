using LupiraCalApi.Dtos.Calendars;
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
            .Produces<List<ContainerDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateCalendarRequest body, CalendarsHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithName("CreateCalendar")
            .WithSummary("Create a calendar. (Address books are managed by LupiraContactApi.)")
            .Produces<ContainerDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
