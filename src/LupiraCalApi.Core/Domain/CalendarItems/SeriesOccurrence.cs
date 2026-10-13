namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>One occurrence of an item: the start it has in the unmodified series (its identity), the start it has now, and
/// its change, if any.</summary>
public sealed record SeriesOccurrence(DateTimeOffset OriginalStart, DateTimeOffset Start, OccurrenceOverride? Change);
