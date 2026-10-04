using Lupira.Contracts.Fires;

namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record ItemActionSet(Guid ItemId, ItemAction Action, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
