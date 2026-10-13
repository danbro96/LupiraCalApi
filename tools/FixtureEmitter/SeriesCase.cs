using LupiraCalApi.Core.Domain.CalendarItems;

internal sealed record SeriesCase(
    string Name, SeriesShape Item, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, IReadOnlyList<SeriesOccurrence> Expected);
