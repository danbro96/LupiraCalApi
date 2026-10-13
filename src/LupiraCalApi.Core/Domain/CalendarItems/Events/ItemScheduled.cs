namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>Created via REST/MCP from structured fields, or as the continuation of a series split at one of its occurrences.</summary>
public sealed record ItemScheduled(Guid ItemId, string ExternalId, CalendarItemFields Fields, ItemDetails? Details,
    Guid? ContinuesItemId = null, DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
