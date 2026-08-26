namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>Fire timing (flattened union): OnStart/OnEnd carry nothing; Offset uses <c>OffsetMinutes</c> (negative = lead time);
/// AllDayAt uses <c>AllDayAt</c> (local wall-clock time on the occurrence date).</summary>
public sealed record PromptFire(PromptFireKind Kind, int? OffsetMinutes, TimeOnly? AllDayAt);
