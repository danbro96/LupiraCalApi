using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>A deterministic, no-LLM action executed directly at fire time.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionKind>))]
public enum ActionKind
{
    SendCheckIn,
    Notify,
    CreateLinkedTask,
    ExpireTarget,
    RescheduleSelf,
    RunJob,
    Rescore,
}
