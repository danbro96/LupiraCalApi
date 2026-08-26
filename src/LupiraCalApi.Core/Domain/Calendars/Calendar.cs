using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.Calendars;

/// <summary>A calendar collection (plain document — its metadata is not versioned). Access is via <see cref="CalendarOwner"/>;
/// membership of items is via the many-to-many <c>CalendarEntry</c> embedded on <see cref="CalendarItem.Calendars"/>.</summary>
public sealed class Calendar
{
    public Guid Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Color { get; set; }

    public string? DefaultTimezone { get; set; }

    public CalendarClass Class { get; set; } = CalendarClass.Agenda;

    public CalendarKind Kind { get; set; } = CalendarKind.Generic;
}
