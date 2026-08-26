namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>Created via REST/MCP from structured fields.</summary>
public sealed record ItemScheduled(Guid ItemId, string ExternalId, CalendarItemFields Fields, ItemDetails? Details);
