using LupiraCalApi.Domain;

namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>A single concrete occurrence of an item within a search window (recurrences expanded).</summary>
public sealed class CalendarItemOccurrenceDto
{
    public required Guid Id { get; set; }
    public string? Title { get; set; }
    public Guid? PlaceId { get; set; }
    public required bool IsAllDay { get; set; }
    public required DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }

    /// <summary>The parent item's completeness (same across its occurrences; null = not applicable), so search results rank directly.</summary>
    public CompletenessScore? Completeness { get; set; }

    public required string Etag { get; set; }
}
