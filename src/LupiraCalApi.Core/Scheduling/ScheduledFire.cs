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
}
