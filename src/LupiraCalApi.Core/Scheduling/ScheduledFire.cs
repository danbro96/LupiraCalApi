namespace LupiraCalApi.Scheduling;

/// <summary>Lifecycle of a materialized fire in <c>cal.scheduled_fire</c>. The materializer only ever writes <c>Pending</c>;
/// the (separate) dispatcher advances the rest. Stored as lowercase text.</summary>
public enum FireStatus { Pending, Claimed, Done, Failed, Expired }

/// <summary>One materialized occurrence of an item's fired payload — a row the materializer upserts into <c>cal.scheduled_fire</c>.
/// Status/attempts are defaulted by the table; <c>DedupeKey</c> (= item + occurrence) makes the upsert idempotent.</summary>
public sealed record ScheduledFireRow(
    Guid Id,
    Guid ItemId,
    Guid CalendarId,
    Guid? PrincipalId,
    DateTimeOffset OccurrenceAt,
    string? PromptRef,
    TimeSpan? ExpireAfter,
    string DedupeKey);

/// <summary>Materializer/dispatcher knobs (personal-scale defaults from the design doc).</summary>
public static class SchedulingDefaults
{
    /// <summary>How far ahead the materializer expands recurring payloads.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(35);

    /// <summary>The nightly sweep cadence that advances the far edge of the horizon.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    /// <summary>Dispatcher claim-loop cadence.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    /// <summary>Rows claimed per tick.</summary>
    public const int ClaimBatch = 50;

    /// <summary>Claim lease; a crashed worker's rows become re-claimable when it lapses.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    /// <summary>Delivery attempts before a row goes <c>failed</c>.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Retry delay after attempt N (1-based); the last step repeats for lease-reclaim overruns.</summary>
    public static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)];
}
