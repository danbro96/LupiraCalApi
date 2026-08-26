namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>Soft delete (tombstone). The stream is never archived so sync stays diffable. <c>At</c> is the recorded
/// deletion time — carried on the event (not read from the clock in Apply) so replay is deterministic.</summary>
public sealed record ItemDeleted(Guid ItemId, DateTimeOffset At);
