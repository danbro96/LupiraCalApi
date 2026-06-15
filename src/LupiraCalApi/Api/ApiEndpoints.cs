using System.Security.Claims;

namespace LupiraCalApi.Api;

/// <summary>
/// The agent + web-UI REST surface under /api (OIDC-protected). Handlers are stubs in Phase 0; the real
/// EventService/ContactService/SearchService implementations land in Phase 1–2, scoped to the caller's own
/// + shared containers. The split into EventsEndpoints/ContactsEndpoints/etc. happens as they grow.
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("ApiPolicy");

        // Verifies a token resolves to a principal (useful for the agent token-exchange smoke test).
        api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            sub = user.FindFirstValue("sub") ?? user.Identity?.Name,
            email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
        }));

        api.MapGet("/events", () => NotYet("events search/list (Phase 1)"));
        api.MapPost("/events", () => NotYet("create event (Phase 1)"));
        api.MapGet("/contacts", () => NotYet("contacts query (Phase 1)"));
        api.MapGet("/calendars", () => NotYet("list accessible calendars + address books (Phase 1)"));

        // MCP transport — Phase 1, mounted here so it inherits the same OIDC policy. Kept LAN-only (not tunneled).
        api.MapMethods("/mcp", ["GET", "POST"], () => NotYet("MCP server (Phase 1)"));

        return app;
    }

    private static IResult NotYet(string what) =>
        Results.Problem($"Not implemented: {what}.", statusCode: StatusCodes.Status501NotImplemented);
}
