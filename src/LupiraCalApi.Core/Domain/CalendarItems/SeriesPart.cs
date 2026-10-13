namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>One side of a split series: where and how long its occurrences run, and the deviations that fall on it.</summary>
public sealed record SeriesPart(
    RecurringSeries Series,
    DateTimeOffset? EndsAt,
    DateOnly? EndDate,
    DateTimeOffset[]? Excluded,
    DateTimeOffset[]? Extra,
    OccurrenceOverride[]? Overrides);
