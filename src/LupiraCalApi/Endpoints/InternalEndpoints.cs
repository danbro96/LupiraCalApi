using LupiraCalApi.Core.Dtos.Internal;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

/// <summary>Service-to-service seams (LAN-only: not tunneled + CF-header backstop). Excluded from the public OpenAPI document.</summary>
public static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapInternal(this IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/items/place-references:check",
                (CheckPlaceReferencesRequest body, InternalItemsHandler h, CancellationToken ct) => h.CheckPlaceReferencesAsync(body, ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription();
        return app;
    }
}
