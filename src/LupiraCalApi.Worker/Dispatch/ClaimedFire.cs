namespace LupiraCalApi.Worker.Dispatch;

/// <summary>One claimed <c>cal.scheduled_fire</c> row (the ClaimSql RETURNING shape). The row is only the clock —
/// the payload is loaded from the item aggregate at dispatch time.</summary>
public sealed record ClaimedFire(
    Guid Id,
    Guid ItemId,
    Guid CalendarId,
    Guid? PrincipalId,
    DateTimeOffset OccurrenceAt,
    string? PromptRef,
    int Attempts,
    string DedupeKey,
    TimeSpan? ExpireAfter);
