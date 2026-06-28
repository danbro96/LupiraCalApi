namespace LupiraCalApi.Domain;

/// <summary>A calendar collection (plain document — its metadata is not versioned). Access is via <see cref="CalendarOwner"/>;
/// membership of items is via the many-to-many <c>CalendarEntry</c> embedded on <see cref="CalendarItem.Calendars"/>.</summary>
public sealed class Calendar
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Color { get; set; }
    public string? DefaultTimezone { get; set; }

    public CalendarClass Class { get; set; } = CalendarClass.Agenda;
    public CalendarKind Kind { get; set; } = CalendarKind.Generic;
}
