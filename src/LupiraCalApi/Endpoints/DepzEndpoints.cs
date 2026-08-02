using LupiraCalApi.Dependencies;

namespace LupiraCalApi.Endpoints;

/// <summary>Non-gating dependency report: this service's outward auth seams, served from the
/// poller's cache. Deliberately not part of /readyz.</summary>
public static class DepzEndpoints
{
    public static void MapDepz(this IEndpointRouteBuilder app) =>
        app.MapGet("/depz", (DependencyReportCache cache) => TypedResults.Ok(cache.Current()))
            .AllowAnonymous()
            .AddEndpointFilter<ProbeKeyFilter>()
            .ExcludeFromDescription()
            .DisableHttpMetrics();
}
