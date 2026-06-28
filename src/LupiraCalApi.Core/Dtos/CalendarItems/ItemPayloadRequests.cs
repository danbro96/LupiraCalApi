using LupiraCalApi.Domain;

namespace LupiraCalApi.Dtos.CalendarItems;

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

    public ItemPrompt ToDomain() => new(Intent, Target, Instruction, Output, Tools, Tier, OnMiss, Fire, Enabled);
}

/// <summary>Set the deterministic payload on an item. Replaces any existing action; rejected (409) if the item carries a prompt.</summary>
public sealed class SetItemActionRequest
{
    public required ActionKind Kind { get; set; }
    public Ref? Target { get; set; }
    public required string ParamsJson { get; set; }
    public required PromptFire Fire { get; set; }
    public bool Enabled { get; set; } = true;

    public ItemAction ToDomain() => new(Kind, Target, ParamsJson, Fire, Enabled);
}
