using LupiraCalApi.Core.Domain.CalendarItems;

namespace LupiraCalApi.Core.Application.Items;

/// <summary>Where one occurrence of a series lands in a search window: its start and end, its start in the unmodified
/// series (null when the series doesn't recur), and its change, if any.</summary>
internal sealed record OccurrenceSlot(DateTimeOffset Start, DateTimeOffset? End, DateTimeOffset? OriginalStart, OccurrenceOverride? Change);
