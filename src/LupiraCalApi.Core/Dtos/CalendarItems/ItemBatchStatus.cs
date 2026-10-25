using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Per-item outcome of a batch create.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemBatchStatus>))]
public enum ItemBatchStatus
{
    Created,
    Existed,
    Forbidden,
    Invalid,
    Unavailable,
}
