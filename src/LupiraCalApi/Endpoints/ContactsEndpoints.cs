using LupiraCalApi.Dtos.Contacts;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContacts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contacts").RequireAuthorization("ApiPolicy").WithTags("Contacts");

        group.MapGet("/", (string? query, Guid? addressBookId, ContactsHandler h, CancellationToken ct) =>
                h.QueryAsync(query, addressBookId, ct))
            .WithName("SearchContacts")
            .WithSummary("Search contacts (full-text + fuzzy name match).")
            .Produces<List<ContactDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateContactRequest body, ContactsHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithName("CreateContact")
            .WithSummary("Create a contact.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", (Guid id, ContactsHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetContact")
            .WithSummary("Get a single contact.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", (Guid id, ReviseContactRequest body, ContactsHandler h, CancellationToken ct) => h.ReviseAsync(id, body, ct))
            .WithName("ReviseContact")
            .WithSummary("Update a contact (merge — provided fields overwrite/append, unmentioned fields are kept).")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", (Guid id, ContactsHandler h, CancellationToken ct) => h.DeleteAsync(id, ct))
            .WithName("DeleteContact")
            .WithSummary("Delete a contact (soft delete + tombstone).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
