using Microsoft.Extensions.Options;

namespace LupiraCalApi.Dependencies;

/// <summary>Sweeps every outward edge on a fixed interval; a bad sweep never kills the loop.</summary>
public sealed class DependencyPollWorker : BackgroundService
{
    private readonly DependencyProbe _probe;
    private readonly IReadOnlyList<DependencyTarget> _targets;
    private readonly DependencyReportCache _cache;
    private readonly DepzOptions _opts;
    private readonly ILogger<DependencyPollWorker> _logger;

    public DependencyPollWorker(
        DependencyProbe probe, IReadOnlyList<DependencyTarget> targets, DependencyReportCache cache,
        IOptions<DepzOptions> opts, ILogger<DependencyPollWorker> logger)
    {
        _probe = probe;
        _targets = targets;
        _cache = cache;
        _opts = opts.Value;
        _logger = logger;
        DependencyTelemetry.ObserveUpFrom(cache);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_opts.StartupDelay, stoppingToken);
            await SweepSafelyAsync(stoppingToken);
            using var timer = new PeriodicTimer(_opts.PollInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepSafelyAsync(stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    private async Task SweepSafelyAsync(CancellationToken ct)
    {
        try
        {
            var results = await Task.WhenAll(_targets.Select(t => _probe.ProbeAsync(t, ct)));
            _cache.Set(new DepzReportDto
            {
                Service = DependencyReportCache.ServiceName,
                LastPolledUtc = DateTimeOffset.UtcNow,
                Dependencies = results,
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dependency sweep failed; next tick retries.");
        }
    }
}
