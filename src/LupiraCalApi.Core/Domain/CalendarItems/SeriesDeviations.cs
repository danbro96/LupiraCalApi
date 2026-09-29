using NodaTime;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>A series' excluded and overridden occurrences, each keyed by the start its occurrence had. Empty = null,
/// ordered by that start — the shape <see cref="CalendarItem"/> keeps them in.</summary>
public sealed record SeriesDeviations(DateTimeOffset[]? Excluded, OccurrenceOverride[]? Overrides)
{
    public static SeriesDeviations Of(CalendarItem item) => new(item.ExcludedOccurrences, item.OccurrenceOverrides);

    /// <summary>The deviations once the series moves from <paramref name="before"/> to <paramref name="after"/> —
    /// a new time, day, zone or rule. Each follows its occurrence: its key shifts by the series' change on the wall
    /// clock (so a zone change alone keeps it on the same local time), and it survives only if the moved series still
    /// has that occurrence. A deviation keyed on one of <paramref name="extras"/> stays put, as extras are absolute
    /// starts; an override's own moved time is the user's choice and stays too.</summary>
    public SeriesDeviations Follow(RecurringSeries before, RecurringSeries after, DateTimeOffset[]? extras, RecurrenceExpander expander)
    {
        if (before == after || (Excluded is null && Overrides is null)) return this;
        if (!after.Recurs || before.Anchor is not { } from || after.Anchor is not { } to) return new SeriesDeviations(null, null);

        var shift = Period.Between(
            from.At.InZone(from.Zone).LocalDateTime, to.At.InZone(to.Zone).LocalDateTime, PeriodUnits.Days | PeriodUnits.AllTimeUnits);

        DateTimeOffset? Rekey(DateTimeOffset original)
        {
            if (extras?.Contains(original) == true) return original;
            var moved = (Instant.FromDateTimeOffset(original).InZone(from.Zone).LocalDateTime + shift).InZoneLeniently(to.Zone).ToDateTimeOffset();
            return expander.Generates(after, moved) ? moved : null;
        }

        var excluded = (Excluded ?? []).Select(Rekey).OfType<DateTimeOffset>().Distinct().Order().ToArray();
        var overrides = (Overrides ?? [])
            .Select(o => Rekey(o.OriginalStart) is { } key ? o with { OriginalStart = key } : null)
            .OfType<OccurrenceOverride>()
            .DistinctBy(o => o.OriginalStart)
            .OrderBy(o => o.OriginalStart)
            .ToArray();
        return new SeriesDeviations(excluded.Length > 0 ? excluded : null, overrides.Length > 0 ? overrides : null);
    }
}
