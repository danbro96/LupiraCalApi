namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>Server-side free-form annotations (JSON). Does not change the ICS or its hash.</summary>
public sealed record ItemMetadataAttached(Guid ItemId, string MetadataJson,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
