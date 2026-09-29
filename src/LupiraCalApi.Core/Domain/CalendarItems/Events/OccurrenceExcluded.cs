namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>One occurrence of a recurring item (by its unmodified start) is removed from the series.</summary>
public sealed record OccurrenceExcluded(Guid ItemId, DateTimeOffset OriginalStart, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
