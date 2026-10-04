using Lupira.Contracts.Fires;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Set the LLM-interpreted payload on an item. Replaces any existing prompt; rejected (409) if the item carries an action.</summary>
public sealed class SetItemPromptRequest
{
    public required PromptIntent Intent { get; set; }

    public Ref? Target { get; set; }

    public required string Instruction { get; set; }

    public required OutputKind Output { get; set; }

    public string[]? Tools { get; set; }

    public ModelTier? Tier { get; set; }

    public FallbackMode OnMiss { get; set; } = FallbackMode.Retry;   // doc default: retry-once → ask

    public required PromptFire Fire { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Client wall-clock of the edit (LWW for the payload section). Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

    public ItemPrompt ToDomain() => new(Intent, Target, Instruction, Output, Tools, Tier, OnMiss, Fire, Enabled);
}
