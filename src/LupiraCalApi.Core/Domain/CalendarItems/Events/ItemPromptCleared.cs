namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record ItemPromptCleared(Guid ItemId, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
