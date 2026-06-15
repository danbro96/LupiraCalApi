using LupiraCalApi.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LupiraCalApi.Health;

/// <summary>Readiness probe (/readyz): the service is ready only when its Postgres DB is reachable.</summary>
public sealed class DatabaseReadyCheck(CalDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("postgres unreachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("postgres error", ex);
        }
    }
}
