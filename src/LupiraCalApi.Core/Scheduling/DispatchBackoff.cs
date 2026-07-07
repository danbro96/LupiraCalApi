namespace LupiraCalApi.Scheduling;

public static class DispatchBackoff
{
    /// <summary>Retry delay after the Nth attempt (1-based); clamps to the last step for lease-reclaim overruns.</summary>
    public static TimeSpan Delay(int attempts) =>
        SchedulingDefaults.Backoff[Math.Clamp(attempts - 1, 0, SchedulingDefaults.Backoff.Length - 1)];
}
