using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.Me;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", (MeHandler h, CancellationToken ct) => h.GetAsync(ct))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Me")
            .WithName("GetMe")
            .WithSummary("The caller's resolved local identity (JIT-provisioned on first login).")
            .Produces<MeDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost("/me/bootstrap", (MeHandler h, CancellationToken ct) => h.BootstrapAsync(ct))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Me")
            .WithName("BootstrapMe")
            .WithSummary("Idempotently ensure the caller has the standard calendar set; returns it.")
            .Produces<List<ContainerDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }
}
