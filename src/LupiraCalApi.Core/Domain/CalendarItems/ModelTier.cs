using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>Model size tier; the LLM gateway maps it to a concrete alias (Small→qwen3-1.7b, Medium→qwen3-14b, Large→gpt-oss-120b).
/// Vendor-neutral and durable across gateway model swaps.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModelTier>))]
public enum ModelTier { Small, Medium, Large }
