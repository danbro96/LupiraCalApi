using LupiraCalApi.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LupiraCalApi.Endpoints;

/// <summary>Liveness (<c>/livez</c>) and readiness (<c>/readyz</c>) probes. Liveness = process-up
/// only; readiness = Postgres reachable.</summary>
public static class HealthChecks
{
    private const string LiveTag = "live";
    private const string ReadyTag = "ready";

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag])
            .AddCheck<DatabaseReadyCheck>(
                "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(3));
        return services;
    }

    public static void MapAppHealthChecks(this IEndpointRouteBuilder app, IHostEnvironment env)
    {
        // Detailed per-dependency JSON only outside Production — the body reveals dependency
        // topology and these probes are anonymous.
        var detailed = !env.IsProduction();

        app.MapHealthChecks("/livez", Options(LiveTag, detailed))
            .AllowAnonymous()
            .DisableHttpMetrics();

        app.MapHealthChecks("/readyz", Options(ReadyTag, detailed))
            .AllowAnonymous()
            .DisableHttpMetrics();
    }

    private static HealthCheckOptions Options(string tag, bool detailed)
    {
        var options = new HealthCheckOptions { Predicate = check => check.Tags.Contains(tag) };
        if (detailed) options.ResponseWriter = WriteJsonReport;
        return options;
    }

    private static Task WriteJsonReport(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                error = e.Value.Exception?.Message,
            }),
        });
}
