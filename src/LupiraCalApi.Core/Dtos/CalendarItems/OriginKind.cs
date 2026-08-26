using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>What a read-time-projected occurrence was synthesized from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OriginKind>))]
public enum OriginKind { Birthday }
