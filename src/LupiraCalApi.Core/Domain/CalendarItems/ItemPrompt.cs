namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>An LLM-interpreted, contracted payload → an agent run. Declared at authoring time; enforced by assistant-api at fire time.</summary>
public sealed record ItemPrompt(
    PromptIntent Intent,
    Ref? Target,
    string Instruction,
    OutputKind Output,
    string[]? Tools,
    ModelTier? Tier,
    FallbackMode OnMiss,
    PromptFire Fire,
    bool Enabled);
