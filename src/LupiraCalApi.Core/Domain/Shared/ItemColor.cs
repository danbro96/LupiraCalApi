using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>An item's own display colour, applied to the whole series. Absent ⇒ the item draws in its calendar's colour.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ItemColor>))]
public enum ItemColor
{
    Red,
    Orange,
    Yellow,
    Green,
    Teal,
    Blue,
    Indigo,
    Purple,
    Pink,
    Brown,
    Graphite,
}
