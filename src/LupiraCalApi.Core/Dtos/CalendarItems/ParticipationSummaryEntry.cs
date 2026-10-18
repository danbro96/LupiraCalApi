namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>One contact's participation across the caller's readable calendars: how many items they took part in that
/// have started, the latest started occurrence's start, and a score — interaction weighted by recency (each past
/// occurrence 0.5^(age / 90 days), the next planned one 1). Declined and withdrawn participations and cancelled items don't
/// count. A ranking signal for pickers/resolvers, not an ACL surface.</summary>
public sealed class ParticipationSummaryEntry
{
    public required Guid ContactId { get; set; }

    public required int Count { get; set; }

    public required DateTimeOffset? LastAt { get; set; }

    public required double Score { get; set; }
}
