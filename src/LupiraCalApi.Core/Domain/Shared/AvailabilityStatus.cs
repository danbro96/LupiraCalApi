using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>A presence/availability segment's status. A day may hold several segments (whole-day or timed).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AvailabilityStatus>))]
public enum AvailabilityStatus
{
    Office,
    Home,
    Vacation,
    Sick,
    Leave,
}
