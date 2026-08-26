namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>A deterministic payload executed directly (no LLM). <c>ParamsJson</c> carries the frozen params (e.g. a SendCheckIn message).</summary>
public sealed record ItemAction(
    ActionKind Kind,
    Ref? Target,
    string ParamsJson,
    PromptFire Fire,
    bool Enabled);
