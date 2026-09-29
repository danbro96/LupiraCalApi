using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Data.Migrations;

public sealed record ItemScheduledV1(Guid ItemId, string ExternalId, CalendarItemFieldsV1 Fields, ItemDetails? Details);
