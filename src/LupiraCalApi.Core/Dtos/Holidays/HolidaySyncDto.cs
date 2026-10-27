using LupiraCalApi.Core.Domain.Holidays;

namespace LupiraCalApi.Core.Dtos.Holidays;

/// <summary>A holiday for an offline mirror, with its all-day dates over the sync window.</summary>
public sealed class HolidaySyncDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required HolidayKind Kind { get; set; }

    /// <summary>The caller's Holidays calendars it shows in.</summary>
    public required Guid[] CalendarIds { get; set; }

    public required List<DateOnly> Dates { get; set; }
}
