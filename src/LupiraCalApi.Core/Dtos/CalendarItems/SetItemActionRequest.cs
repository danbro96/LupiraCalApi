using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Set the deterministic payload on an item. Replaces any existing action; rejected (409) if the item carries a prompt.</summary>
public sealed class SetItemActionRequest
{
    public required ActionKind Kind { get; set; }
    public Ref? Target { get; set; }
    public required string ParamsJson { get; set; }
    public required PromptFire Fire { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Client wall-clock of the edit (LWW for the payload section). Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

    public ItemAction ToDomain() => new(Kind, Target, ParamsJson, Fire, Enabled);
}
