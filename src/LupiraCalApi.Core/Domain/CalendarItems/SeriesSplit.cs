using Lupira.Primitives;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>A series cut at one of its rule's occurrences (not the first): <see cref="Earlier"/> keeps every occurrence before
/// it, its rule ending there, and <see cref="Following"/> starts at it with the rest of the rule, at the series' length.
/// Excluded, extra and changed occurrences go with the part their original start falls in, so the parts' occurrences together
/// are the series'.</summary>
public sealed record SeriesSplit(SeriesPart Earlier, SeriesPart Following)
{
    /// <summary>Null unless <paramref name="at"/> is an occurrence of the item's rule after its first.</summary>
    public static SeriesSplit? At(CalendarItem item, DateTimeOffset at, RecurrenceExpander expander)
    {
        var series = RecurringSeries.Of(item);
        if (!series.Recurs || !expander.Generates(series, at)) return null;
        if (expander.Before(series, at) is not (var earlierCount, { } earlierLast)) return null;

        var (earlierRule, followingRule) = RecurrenceRuleBounds.Count(series.RecurrenceRule) is { } count
            ? (RecurrenceRuleBounds.WithCount(series.RecurrenceRule, earlierCount), RecurrenceRuleBounds.WithCount(series.RecurrenceRule, count - earlierCount))
            : (RecurrenceRuleBounds.WithLastStart(series.RecurrenceRule, earlierLast, series.IsAllDay), series.RecurrenceRule);

        var day = DateOnly.FromDateTime(at.UtcDateTime);
        var (following, endsAt, endDate) = series.IsAllDay
            ? (series with { StartDate = day, RecurrenceRule = followingRule }, (DateTimeOffset?) null,
                item.EndDate is { } end && item.StartDate is { } start ? day.AddDays(end.DayNumber - start.DayNumber) : (DateOnly?) null)
            : (series with { StartsAt = at, RecurrenceRule = followingRule }, item.EndsAt - item.StartsAt is { } length ? at + length : (DateTimeOffset?) null, item.EndDate);

        bool Before(DateTimeOffset start) => start < at;
        return new SeriesSplit(
            new SeriesPart(series with { RecurrenceRule = earlierRule }, item.EndsAt, item.EndDate,
                Part(item.ExcludedOccurrences, Before), Part(item.ExtraOccurrences, Before), Part(item.OccurrenceOverrides, o => Before(o.OriginalStart))),
            new SeriesPart(following, endsAt, endDate,
                Part(item.ExcludedOccurrences, s => !Before(s)), Part(item.ExtraOccurrences, s => !Before(s)), Part(item.OccurrenceOverrides, o => !Before(o.OriginalStart))));
    }

    /// <summary>The id a participation gets on the series continuing its item, derivable by an offline client.</summary>
    public static Guid ContinuedParticipationId(Guid continuationId, Guid participationId) =>
        DeterministicGuid.From($"participation:{continuationId}:{participationId}");

    private static T[]? Part<T>(T[]? all, Func<T, bool> belongs) => all?.Where(belongs).ToArray() is { Length: > 0 } part ? part : null;
}
