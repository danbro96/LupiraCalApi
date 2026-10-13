using LupiraCalApi.Core.Domain.CalendarItems;

internal sealed record SeriesShape(
    bool IsAllDay, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, DateOnly? StartDate, DateOnly? EndDate, string? StartTimezone, string? RecurrenceRule,
    DateTimeOffset[]? ExcludedOccurrences = null, DateTimeOffset[]? ExtraOccurrences = null, OccurrenceOverride[]? OccurrenceOverrides = null)
{
    public static SeriesShape Of(SeriesPart part) =>
        new(part.Series.IsAllDay, part.Series.StartsAt, part.EndsAt, part.Series.StartDate, part.EndDate, part.Series.StartTimezone, part.Series.RecurrenceRule,
            part.Excluded, part.Extra, part.Overrides);

    public RecurringSeries ToSeries() => new(IsAllDay, StartsAt, StartDate, StartTimezone, RecurrenceRule);

    public CalendarItem ToItem() => new()
    {
        IsAllDay = IsAllDay, StartsAt = StartsAt, EndsAt = EndsAt, StartDate = StartDate, EndDate = EndDate, StartTimezone = StartTimezone,
        RecurrenceRule = RecurrenceRule, ExcludedOccurrences = ExcludedOccurrences, ExtraOccurrences = ExtraOccurrences, OccurrenceOverrides = OccurrenceOverrides,
    };
}
