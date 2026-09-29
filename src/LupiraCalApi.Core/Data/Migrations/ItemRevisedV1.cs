using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Data.Migrations;

public sealed record ItemRevisedV1(Guid ItemId, CalendarItemFieldsV1 Fields, ItemDetails? Details,
    DateTimeOffset? OccurredAt = null, Guid? CommandId = null);
