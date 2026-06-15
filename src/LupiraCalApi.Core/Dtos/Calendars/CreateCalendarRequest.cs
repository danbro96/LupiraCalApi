namespace LupiraCalApi.Dtos.Calendars;

public record CreateCalendarRequest(string Slug, string? DisplayName, string Kind, string? Color, string? DefaultTimezone);
