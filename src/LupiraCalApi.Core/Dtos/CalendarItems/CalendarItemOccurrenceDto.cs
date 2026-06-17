namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>A single concrete occurrence of an item within a search window (recurrences expanded).</summary>
public record CalendarItemOccurrenceDto(
    Guid Id, string? Title, Guid? PlaceId, bool IsAllDay, DateTimeOffset Start, DateTimeOffset? End, string Etag);
