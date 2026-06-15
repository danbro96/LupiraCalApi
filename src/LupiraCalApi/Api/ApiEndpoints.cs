using System.Security.Claims;
using System.Text.Json.Nodes;
using LupiraCalApi.Domain;

namespace LupiraCalApi.Api;

/// <summary>The agent + web-UI REST surface under /api (OIDC-protected). Handlers resolve the caller via
/// <see cref="IUserContext"/> and delegate to the domain services, which enforce container-scoped access.</summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("ApiPolicy");

        api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            sub = user.FindFirstValue("sub") ?? user.Identity?.Name,
            email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
        }));

        // Containers
        api.MapGet("/calendars", async (IUserContext uc, CalendarService cals) =>
            Results.Ok(await cals.ListContainersAsync((await uc.GetCurrentUserAsync()).Id)));
        api.MapPost("/calendars", async (IUserContext uc, CalendarService cals, CreateCalendarRequest req) =>
            Results.Ok(await cals.CreateAsync((await uc.GetCurrentUserAsync()).Id, req)));

        // Events
        api.MapGet("/events", async (IUserContext uc, EventService ev,
            string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, string? tag, string? metadata) =>
            Results.Ok(await ev.SearchAsync((await uc.GetCurrentUserAsync()).Id, query, from, to, calendarId, tag, metadata)));
        api.MapPost("/events", async (IUserContext uc, EventService ev, CreateEventRequest req) =>
            Results.Ok(await ev.CreateAsync((await uc.GetCurrentUserAsync()).Id, req)));
        api.MapGet("/events/{id:guid}", async (IUserContext uc, EventService ev, Guid id) =>
        {
            var dto = await ev.GetAsync((await uc.GetCurrentUserAsync()).Id, id);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });
        api.MapPut("/events/{id:guid}", async (IUserContext uc, EventService ev, Guid id, UpdateEventRequest req) =>
            Results.Ok(await ev.UpdateAsync((await uc.GetCurrentUserAsync()).Id, id, req)));
        api.MapDelete("/events/{id:guid}", async (IUserContext uc, EventService ev, Guid id) =>
        {
            await ev.DeleteAsync((await uc.GetCurrentUserAsync()).Id, id);
            return Results.NoContent();
        });
        api.MapPost("/events/{id:guid}/metadata", async (IUserContext uc, EventService ev, Guid id, JsonNode patch) =>
            Results.Ok(await ev.AttachMetadataAsync((await uc.GetCurrentUserAsync()).Id, id, patch)));

        // Cross-domain relations (e.g. event ↔ LupiraTasks item)
        api.MapPost("/events/{id:guid}/relations", async (IUserContext uc, RelationService rel, Guid id, CreateRelationRequest req) =>
            Results.Ok(await rel.LinkEventAsync((await uc.GetCurrentUserAsync()).Id, id, req)));
        api.MapGet("/events/{id:guid}/relations", async (IUserContext uc, RelationService rel, Guid id) =>
            Results.Ok(await rel.ListForEventAsync((await uc.GetCurrentUserAsync()).Id, id)));
        api.MapGet("/relations", async (IUserContext uc, RelationService rel, string toKind, string toRef) =>
            Results.Ok(await rel.FindEventsLinkedToAsync((await uc.GetCurrentUserAsync()).Id, toKind, toRef)));

        // Contacts
        api.MapGet("/contacts", async (IUserContext uc, ContactService cs, string? query, Guid? addressBookId) =>
            Results.Ok(await cs.QueryAsync((await uc.GetCurrentUserAsync()).Id, query, addressBookId)));
        api.MapPost("/contacts", async (IUserContext uc, ContactService cs, CreateContactRequest req) =>
            Results.Ok(await cs.CreateAsync((await uc.GetCurrentUserAsync()).Id, req)));
        api.MapGet("/contacts/{id:guid}", async (IUserContext uc, ContactService cs, Guid id) =>
        {
            var dto = await cs.GetAsync((await uc.GetCurrentUserAsync()).Id, id);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });
        api.MapDelete("/contacts/{id:guid}", async (IUserContext uc, ContactService cs, Guid id) =>
        {
            await cs.DeleteAsync((await uc.GetCurrentUserAsync()).Id, id);
            return Results.NoContent();
        });

        return app;
    }
}
