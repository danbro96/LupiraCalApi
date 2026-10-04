using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.Calendars;

public sealed class CreateCalendarRequest
{
    public required string Slug { get; set; }

    public string? DisplayName { get; set; }

    public required string Type { get; set; }

    public string? Color { get; set; }

    /// <summary>IANA zone id for items written without one; omitted = the server default.</summary>
    public string? DefaultTimezone { get; set; }

    /// <summary>Calendars only; defaults to Agenda/Generic. Ignored for address books.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<CalendarClass>))]
    public CalendarClass? Class { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<CalendarKind>))]
    public CalendarKind? Kind { get; set; }
}
