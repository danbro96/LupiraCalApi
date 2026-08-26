namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record ItemActionCleared(Guid ItemId, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
