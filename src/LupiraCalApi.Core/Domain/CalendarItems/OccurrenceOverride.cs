using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>A change to one occurrence of a recurring item, identified by the start it would have had unchanged
/// (<see cref="OriginalStart"/>, UTC; an all-day series uses the date at 00:00Z). Null members inherit from the series;
/// <see cref="ItemStatus.Cancelled"/> drops the occurrence.</summary>
public sealed record OccurrenceOverride(
    DateTimeOffset OriginalStart,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Title,
    string? Description,
    ItemStatus? Status,
    string? LocationLabel);
