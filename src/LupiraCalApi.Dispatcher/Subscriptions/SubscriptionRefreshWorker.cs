using LupiraCalApi.Core.Application.Subscriptions;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Dispatcher.Subscriptions;

/// <summary>
/// The feed refresh loop: one <see cref="SubscriptionRefreshService.RunTickAsync"/> per tick, each in its own DI scope and
/// span. Ticks immediately after a small startup jitter, then every Tick.
/// </summary>
public sealed class SubscriptionRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SubscriptionOptions> options,
    ILogger<SubscriptionRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        logger.LogInformation("Subscription refresh started: tick {Tick}, batch {Batch}, lease {Lease}, interval {Interval}.", opts.Tick, opts.BatchSize, opts.Lease, opts.RefreshInterval);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(2, 6)), stoppingToken);

            using var timer = new PeriodicTimer(opts.Tick);
            do
            {
                using var activity = FireDispatchWorker.Activity.StartActivity("subscription.tick");
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<SubscriptionRefreshService>();
                    var claimed = await service.RunTickAsync(stoppingToken);
                    activity?.SetTag("subscription.claimed", claimed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad tick never kills the loop.
                    logger.LogError(ex, "Subscription refresh tick failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
