using Marten;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LupiraCalApi.Health;

/// <summary>Readiness probe (/readyz): the service is ready only when its Postgres (Marten) store is reachable.</summary>
public sealed class DatabaseReadyCheck(IDocumentStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var session = store.LightweightSession();
            await session.QueryAsync<int>("select 1", ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("postgres error", ex);
        }
    }
}
