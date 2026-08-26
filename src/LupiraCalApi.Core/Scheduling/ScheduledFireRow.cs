namespace LupiraCalApi.Core.Scheduling;

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
