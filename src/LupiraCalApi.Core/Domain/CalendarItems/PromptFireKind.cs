using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>When a payload fires relative to its item's occurrence.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PromptFireKind>))]
public enum PromptFireKind { OnStart, OnEnd, Offset, AllDayAt }
