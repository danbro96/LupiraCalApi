namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>Created or replaced from a DAV PUT — parsed into structured fields (no blob retained).</summary>
public sealed record ItemImported(Guid ItemId, string ExternalId, CalendarItemFields Parsed);
