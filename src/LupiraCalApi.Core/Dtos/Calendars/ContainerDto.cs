using LupiraCalApi.Domain;
using System.Text.Json.Serialization;

namespace LupiraCalApi.Dtos.Calendars;

/// <summary>A calendar or address book the caller can access (<c>type</c> discriminates; access = the caller's grant level).
/// <c>Class</c>/<c>Kind</c> classify calendars only (null for address books).</summary>
public sealed class ContainerDto
{
    public required Guid Id { get; set; }
    public required string Type { get; set; }
    public required string Slug { get; set; }
    public string? DisplayName { get; set; }
    public string? Color { get; set; }
    public string? DefaultTimezone { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<CalendarClass>))]
    public CalendarClass? Class { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<CalendarKind>))]
    public CalendarKind? Kind { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<Access>))]
    public required Access Access { get; set; }
}
