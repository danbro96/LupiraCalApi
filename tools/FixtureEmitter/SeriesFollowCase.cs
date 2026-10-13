using LupiraCalApi.Core.Domain.CalendarItems;

internal sealed record SeriesFollowCase(
    string Name, SeriesShape Before, SeriesShape After, string? ExpectedRule, DateTimeOffset[]? ExpectedExcluded, OccurrenceOverride[]? ExpectedOverrides);
