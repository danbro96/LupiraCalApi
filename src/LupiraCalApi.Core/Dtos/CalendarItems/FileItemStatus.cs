using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Per-entry outcome of a batch file.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FileItemStatus>))]
public enum FileItemStatus
{
    Filed,
    NotFound,
    Forbidden,
    Conflict,
    Unavailable,
    Invalid,
}
