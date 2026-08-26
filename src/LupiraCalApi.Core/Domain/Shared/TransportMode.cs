using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Domain.Shared;

/// <summary>Mode of a <see cref="TravelLeg"/>. Rail split: <c>Train</c> = mainline/commuter, <c>Metro</c> = rapid transit,
/// <c>Tram</c> = light/narrow-gauge rail; <c>Coach</c> = long-distance bus vs local <c>Bus</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransportMode>))]
public enum TransportMode
{
    Flight,
    Train,
    Metro,
    Tram,
    Bus,
    Coach,
    Car,
    Ferry,
    Bike,
    Walk,
    Other,
}
