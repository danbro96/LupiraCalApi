using System.Diagnostics;
using LupiraCalApi.Scheduling;
using LupiraCalApi.Worker.Dispatch;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Worker;

/// <summary>
/// The claim loop: one <see cref="FireDispatchService.RunTickAsync"/> per tick, each in its own DI scope and span.
/// Ticks immediately after a small startup jitter (catch-up shouldn't wait a full tick), then every Tick.
/// </summary>
public sealed class FireDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    IOptions<DispatcherOptions> options,
    ILogger<FireDispatchWorker> logger) : BackgroundService
{
    public static readonly ActivitySource Activity = new("LupiraCalApi.Worker");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Covers a plain `dotnet run` before cal-api ever started; also applies the principal_id alter on upgrades.
        await ScheduledFireSchema.EnsureExistsAsync(
            config.GetConnectionString("Postgres") ?? CoreServiceCollectionExtensions.DefaultConnectionString, stoppingToken);

        var opts = options.Value;
        logger.LogInformation("Fire dispatcher started: tick {Tick}s, batch {Batch}, lease {Lease}s, max {Max} attempts.",
            opts.TickSeconds, opts.BatchSize, opts.LeaseSeconds, opts.MaxAttempts);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(2, 6)), stoppingToken);

            using var timer = new PeriodicTimer(opts.Tick);
            do
            {
                using var activity = Activity.StartActivity("dispatch.tick");
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<FireDispatchService>();
                    var claimed = await service.RunTickAsync(stoppingToken);
                    activity?.SetTag("dispatch.claimed", claimed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad tick never kills the loop.
                    logger.LogError(ex, "Dispatch tick failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
