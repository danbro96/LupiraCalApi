using LupiraCalApi.Scheduling;

namespace LupiraCalApi.Worker.Dispatch;

/// <summary>Binds <c>Dispatcher</c>; defaults come from the design doc via <see cref="SchedulingDefaults"/>.</summary>
public sealed class DispatcherOptions
{
    public const string SectionName = "Dispatcher";

    public int TickSeconds { get; set; } = (int)SchedulingDefaults.Tick.TotalSeconds;
    public int BatchSize { get; set; } = SchedulingDefaults.ClaimBatch;
    public int LeaseSeconds { get; set; } = (int)SchedulingDefaults.Lease.TotalSeconds;
    public int MaxAttempts { get; set; } = SchedulingDefaults.MaxAttempts;

    public TimeSpan Tick => TimeSpan.FromSeconds(TickSeconds);
    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);
}
