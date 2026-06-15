using LupiraCalApi.Dtos.Me;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", (MeHandler h, CancellationToken ct) => h.GetAsync(ct))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Me")
            .WithSummary("The caller's resolved local identity (JIT-provisioned on first login).")
            .Produces<MeDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }
}
