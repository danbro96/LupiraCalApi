namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>The outcome of changing following occurrences: the series up to them (null when none is left) and the series
/// that continues from them (null when they were deleted).</summary>
public sealed class SeriesSplitDto
{
    public CalendarItemDto? Earlier { get; set; }

    public CalendarItemDto? Following { get; set; }
}
