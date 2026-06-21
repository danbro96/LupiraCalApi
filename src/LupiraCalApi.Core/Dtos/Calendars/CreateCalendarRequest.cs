namespace LupiraCalApi.Dtos.Calendars;

public sealed class CreateCalendarRequest
{
    public required string Slug { get; set; }
    public string? DisplayName { get; set; }
    public required string Kind { get; set; }
    public string? Color { get; set; }
    public string? DefaultTimezone { get; set; }
}
