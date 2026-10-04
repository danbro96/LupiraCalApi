using Lupira.Contracts.Fires;

namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

public sealed record ItemPromptSet(Guid ItemId, ItemPrompt Prompt, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
