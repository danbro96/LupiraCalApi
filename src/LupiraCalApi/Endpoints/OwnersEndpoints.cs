using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

/// <summary>Share a container by granting/revoking co-owners. Owner-only; the target is identified by login email
/// (provisioned if they have not logged in yet). Split routes for calendars and address books.</summary>
public static class OwnersEndpoints
{
    public static IEndpointRouteBuilder MapOwners(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").RequireAuthorization("ApiPolicy").WithTags("Owners");

        group.MapPost("/calendars/{calendarId:guid}/owners", (Guid calendarId, GrantOwnerRequest body, CalendarsHandler h, CancellationToken ct) => h.GrantCalendarOwnerAsync(calendarId, body, ct))
            .WithSummary("Grant a member access to a calendar (access = owner|read-write|read; default owner).")
            .Produces<OwnerGrantDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/calendars/{calendarId:guid}/owners", (Guid calendarId, string email, CalendarsHandler h, CancellationToken ct) => h.RevokeCalendarOwnerAsync(calendarId, email, ct))
            .WithSummary("Revoke a member's access to a calendar (by email). 409 if it would remove the last owner.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/address-books/{addressBookId:guid}/owners", (Guid addressBookId, GrantOwnerRequest body, CalendarsHandler h, CancellationToken ct) => h.GrantAddressBookOwnerAsync(addressBookId, body, ct))
            .WithSummary("Grant a member access to an address book (access = owner|read-write|read; default owner).")
            .Produces<OwnerGrantDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/address-books/{addressBookId:guid}/owners", (Guid addressBookId, string email, CalendarsHandler h, CancellationToken ct) => h.RevokeAddressBookOwnerAsync(addressBookId, email, ct))
            .WithSummary("Revoke a member's access to an address book (by email). 409 if it would remove the last owner.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
