using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.CalendarItems;

/// <summary>A presence/availability segment's status; the span is the item itself (whole-day or timed). Replaces the old
/// Availability item kind — presence items live on the availability calendar and are exempt from completeness scoring.</summary>
public sealed record PresenceDetail(AvailabilityStatus Status);
