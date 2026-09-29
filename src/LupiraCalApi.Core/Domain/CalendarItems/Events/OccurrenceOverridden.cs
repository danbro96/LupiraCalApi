namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>One occurrence of a recurring item deviates from the series; replaces any earlier change to it.</summary>
public sealed record OccurrenceOverridden(Guid ItemId, OccurrenceOverride Override, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
