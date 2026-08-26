using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Completeness;

/// <summary>A field is fully present, weak/partial (0.5), or absent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GapSeverity>))]
public enum GapSeverity
{
    Weak,
    Absent,
}
