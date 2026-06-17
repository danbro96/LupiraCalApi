using LupiraCalApi.Dtos.Contacts;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class ContactGroupsEndpoints
{
    public static IEndpointRouteBuilder MapContactGroups(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("ApiPolicy").WithTags("ContactGroups");

        group.MapGet("/address-books/{addressBookId:guid}/groups", (Guid addressBookId, ContactGroupsHandler h, CancellationToken ct) => h.ListAsync(addressBookId, ct))
            .WithSummary("List groups (personal groupings + organizations) in an address book.")
            .Produces<List<ContactGroupDto>>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/address-books/{addressBookId:guid}/groups", (Guid addressBookId, string? kind, string name, ContactGroupsHandler h, CancellationToken ct) => h.CreateAsync(addressBookId, kind, name, ct))
            .WithSummary("Create a group. kind = group|organization (an employer is an organization-kind group).")
            .Produces<ContactGroupDto>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/groups/{groupId:guid}", (Guid groupId, string name, ContactGroupsHandler h, CancellationToken ct) => h.RenameAsync(groupId, name, ct))
            .WithSummary("Rename a group.")
            .Produces<ContactGroupDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapPost("/groups/{groupId:guid}/members", (Guid groupId, Guid contactId, ContactGroupsHandler h, CancellationToken ct) => h.AddMemberAsync(groupId, contactId, ct))
            .WithSummary("Add a contact to a group.")
            .Produces<ContactGroupDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/groups/{groupId:guid}/members/{contactId:guid}", (Guid groupId, Guid contactId, ContactGroupsHandler h, CancellationToken ct) => h.RemoveMemberAsync(groupId, contactId, ct))
            .WithSummary("Remove a contact from a group.")
            .Produces<ContactGroupDto>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/groups/{groupId:guid}", (Guid groupId, ContactGroupsHandler h, CancellationToken ct) => h.DeleteAsync(groupId, ct))
            .WithSummary("Delete a group.")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
