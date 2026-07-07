using LupiraCalApi.Domain;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LupiraCalApi.Scheduling;

/// <summary>Ensures <c>cal.scheduled_fire</c> exists before the async daemon starts (the deploy step runs <c>--apply-schema</c>;
/// this covers a plain <c>dotnet run</c>). Registered first so its StartAsync completes before the daemon's.</summary>
public sealed class ScheduledFireTableInitializer(IConfiguration config) : IHostedService
{
    public Task StartAsync(CancellationToken ct) =>
        ScheduledFireSchema.EnsureExistsAsync(
            config.GetConnectionString("Postgres") ?? CoreServiceCollectionExtensions.DefaultConnectionString, ct);

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Nightly horizon-extend: re-materializes payload-bearing items (recurring AND one-shots beyond the window at
/// set-time) so the rolling 35-day window keeps its far edge as days pass. Insert-only (idempotent on dedupe_key);
/// event-driven (re)materialization is the projection's job.</summary>
public sealed class HorizonSweep(IDocumentStore store, IFireMaterializer materializer, ILogger<HorizonSweep> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(SchedulingDefaults.SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))   // first sweep one interval in; the daemon covers initial materialization
            {
                try { await SweepAsync(DateTimeOffset.UtcNow, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Horizon sweep failed"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task SweepAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var session = store.LightweightSession();
        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        foreach (var item in candidates.Where(i => i.Prompt is not null || i.Action is not null))
        {
            var context = await SchedulingQueries.FireContextAsync(session, item, ct);
            foreach (var r in materializer.Materialize(item, context, now, SchedulingDefaults.Horizon))
                session.QueueSqlCommand(ScheduledFireSchema.InsertSql,
                    r.Id, r.ItemId, r.CalendarId, (object?)r.PrincipalId ?? DBNull.Value, r.OccurrenceAt,
                    (object?)r.PromptRef ?? DBNull.Value, (object?)r.ExpireAfter ?? DBNull.Value, r.DedupeKey);
        }
        await session.SaveChangesAsync(ct);
    }
}
