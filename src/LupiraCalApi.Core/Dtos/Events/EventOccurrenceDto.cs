namespace LupiraCalApi.Dtos.Events;

/// <summary>A single concrete occurrence of an event within a search window (recurrences expanded).</summary>
public record EventOccurrenceDto(
    Guid Id, Guid CalendarId, string? Title, string? Location, bool IsAllDay,
    DateTimeOffset Start, DateTimeOffset? End, string Etag);
