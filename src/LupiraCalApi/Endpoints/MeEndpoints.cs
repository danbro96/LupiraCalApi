using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Me;
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
            .Produces<MeDto>(StatusCodes.Status200OK);

        app.MapPost("/me/bootstrap", (BootstrapRequest? body, MeHandler h, CancellationToken ct) => h.BootstrapAsync(body, ct))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Me")
            .WithName("BootstrapMe")
            .WithSummary("Idempotently ensure the caller has the standard calendar set; returns it. Calendars it creates get the optional body's DefaultTimezone (IANA id), else the server default; existing calendars are unchanged.")
            .Produces<List<ContainerDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        return app;
    }
}
