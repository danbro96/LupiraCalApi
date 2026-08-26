namespace LupiraCalApi.Core.Domain.CalendarItems.Events;

/// <summary>Structured update of any subset of fields/details.</summary>
public sealed record ItemRevised(Guid ItemId, CalendarItemFields Fields, ItemDetails? Details,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
