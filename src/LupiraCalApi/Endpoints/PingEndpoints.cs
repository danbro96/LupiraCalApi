using System.Security.Claims;
using LupiraCalApi.Dtos.Ping;

namespace LupiraCalApi.Endpoints;

public static class PingEndpoints
{
    public static IEndpointRouteBuilder MapPing(this IEndpointRouteBuilder app)
    {
        // Claims echo only — deliberately no CurrentUser, so a probe never provisions a Principal
        // or bumps LastSeenAt. Consumers poll this from /depz to verify the auth seam.
        app.MapGet("/pingz", (ClaimsPrincipal user) => TypedResults.Ok(new PingDto
            {
                Subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
                Audiences = user.FindAll("aud").Select(c => c.Value).ToArray(),
                Email = user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email),
            }))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Ping")
            .WithName("Ping")
            .WithSummary("Authenticated claims echo for dependency probes; resolves nothing, writes nothing.")
            .Produces<PingDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .DisableHttpMetrics();
        return app;
    }
}
