using NodaTime;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>How a series moved on the wall clock: from its old first start, read in its old zone, to its new one in its new
/// zone. Applied to one of its starts it gives where that occurrence starts in the moved series.</summary>
internal sealed record SeriesShift(DateTimeZone From, DateTimeZone To, Period By)
{
    public static SeriesShift? Between(RecurringSeries before, RecurringSeries after) =>
        before.Anchor is { } from && after.Anchor is { } to
            ? new SeriesShift(from.Zone, to.Zone, Period.Between(
                from.At.InZone(from.Zone).LocalDateTime, to.At.InZone(to.Zone).LocalDateTime, PeriodUnits.Days | PeriodUnits.AllTimeUnits))
            : null;

    public DateTimeOffset Apply(DateTimeOffset start) =>
        (Instant.FromDateTimeOffset(start).InZone(From).LocalDateTime + By).InZoneLeniently(To).ToDateTimeOffset().ToUniversalTime();

    public LocalDateTime Apply(LocalDateTime wallClock) => wallClock + By;
}
