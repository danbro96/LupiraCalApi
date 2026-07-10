using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

/// <summary>Share a calendar by granting/revoking co-owners. Owner-only; the target is identified by login email
/// (provisioned if they have not logged in yet).</summary>
public static class OwnersEndpoints
{
    public static IEndpointRouteBuilder MapOwners(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").RequireAuthorization("ApiPolicy").WithTags("Owners");

        group.MapPost("/calendars/{calendarId:guid}/owners", (Guid calendarId, GrantOwnerRequest body, CalendarsHandler h, CancellationToken ct) => h.GrantCalendarOwnerAsync(calendarId, body, ct))
            .WithName("GrantCalendarOwner")
            .WithSummary("Grant a member access to a calendar (access = owner|read-write|read; default owner).")
            .Produces<OwnerGrantDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/calendars/{calendarId:guid}/owners", (Guid calendarId, string email, CalendarsHandler h, CancellationToken ct) => h.RevokeCalendarOwnerAsync(calendarId, email, ct))
            .WithName("RevokeCalendarOwner")
            .WithSummary("Revoke a member's access to a calendar (by email). 409 if it would remove the last owner.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
