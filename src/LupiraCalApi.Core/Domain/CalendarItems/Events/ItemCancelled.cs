namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>STATUS → cancelled (distinct from deletion; the item still exists).</summary>
public sealed record ItemCancelled(Guid ItemId, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
