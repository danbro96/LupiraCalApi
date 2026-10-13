using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>How a participant wants to be reminded of an item: the device's default, never, or a set lead time.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReminderMode>))]
public enum ReminderMode
{
    Default,
    Off,
    Before,
}
