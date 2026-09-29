namespace LupiraCalApi.Core.Data.Migrations;

public sealed record ItemImportedV1(Guid ItemId, string ExternalId, CalendarItemFieldsV1 Parsed);
