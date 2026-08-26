using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>What an LLM-interpreted run should accomplish.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PromptIntent>))]
public enum PromptIntent
{
    EnrichRecord,
    Research,
    CreateFollowUp,
    Monitor,
    Summarise,
    AskUser,
}
