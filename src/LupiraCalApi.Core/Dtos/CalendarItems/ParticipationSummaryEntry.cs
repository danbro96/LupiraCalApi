namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>One contact's participation across the caller's readable calendars: how many items they attend(ed), the most
/// recent occurrence start (past or planned), and a score — interaction weighted by recency (each past
/// occurrence 0.5^(age / 90 days), the next planned one 1). A ranking signal for pickers/resolvers, not an ACL surface.</summary>
public sealed record ParticipationSummaryEntry(Guid ContactId, int Count, DateTimeOffset? LastAt, double Score);
