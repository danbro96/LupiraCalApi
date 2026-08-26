using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>What a <see cref="Ref"/> points at.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RefKind>))]
public enum RefKind
{
    Event,
    Contact,
    Task,
    External,
}
