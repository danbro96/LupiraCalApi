using Lupira.Primitives;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Domain.Holidays;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Holidays;

namespace LupiraCalApi.Core.Application.Holidays;

/// <summary>Projects the Holidays calendars' computed holidays at read time, like birthdays: nothing is stored.</summary>
public sealed class HolidayOccurrenceReader(TimeProvider clock)
{
    public List<CalendarItemOccurrenceDto> Occurrences(Guid[] calendarIds, string? query, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        if (calendarIds.Length == 0 || windowEnd <= windowStart) return [];
        var term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var results = new List<CalendarItemOccurrenceDto>();
        foreach (var h in Between(windowStart.Year, windowEnd.Year))
        {
            if (term is not null && !h.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
            var start = CalendarItemService.AllDayInstant(h.Date);
            if (start < windowStart || start >= windowEnd) continue;

            var id = IdOf(h.Key);
            results.Add(new CalendarItemOccurrenceDto
            {
                Id = id,
                Title = h.Name,
                IsAllDay = true,
                Start = start,
                End = start,
                IsChanged = false,
                CalendarIds = calendarIds,
                Status = ItemStatus.Confirmed,
                ChildCount = 0,
                Origin = new OccurrenceOrigin { Kind = OriginKind.Holiday, SourceId = id },
                Holiday = h.Kind,
                Etag = $"holiday-{h.Key}-{h.Date:yyyyMMdd}",
            });
        }

        return results;
    }

    /// <summary>Every holiday from last year through two years ahead, the horizon the mirror keeps birthdays over.</summary>
    public List<HolidaySyncDto> Snapshot(Guid[] calendarIds)
    {
        if (calendarIds.Length == 0) return [];
        var year = clock.GetUtcNow().Year;
        return [.. Between(year - 1, year + 2).GroupBy(h => h.Key).Select(g => new HolidaySyncDto
        {
            Id = IdOf(g.Key),
            Name = g.First().Name,
            Kind = g.First().Kind,
            CalendarIds = calendarIds,
            Dates = [.. g.Select(h => h.Date)],
        })];
    }

    private static IEnumerable<Holiday> Between(int fromYear, int toYear)
    {
        for (var year = Math.Max(fromYear, SwedishHolidays.FirstYear); year <= toYear; year++)
            foreach (var h in SwedishHolidays.For(year))
                yield return h;
    }

    private static Guid IdOf(string key) => DeterministicGuid.From($"holiday:{key}");
}
