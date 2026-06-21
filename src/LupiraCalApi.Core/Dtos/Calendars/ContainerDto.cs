using LupiraCalApi.Domain;
using System.Text.Json.Serialization;

namespace LupiraCalApi.Dtos.Calendars;

/// <summary>A calendar or address book the caller can access (kind discriminates; access = the caller's grant level).</summary>
public sealed class ContainerDto
{
    public required Guid Id { get; set; }
    public required string Kind { get; set; }
    public required string Slug { get; set; }
    public string? DisplayName { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<Access>))]
    public required Access Access { get; set; }
}
