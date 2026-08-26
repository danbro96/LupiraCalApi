namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>Resurrects a soft-deleted item (e.g. DELETE-then-PUT of the same uid).</summary>
public sealed record ItemRestored(Guid ItemId);
