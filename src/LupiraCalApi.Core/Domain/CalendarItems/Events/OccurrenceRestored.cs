namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>One occurrence of a recurring item reverts to the series (its exclusion or override is dropped).</summary>
public sealed record OccurrenceRestored(Guid ItemId, DateTimeOffset OriginalStart, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
