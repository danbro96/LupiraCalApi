using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Domain.Subscriptions;

namespace LupiraCalApi.Core.Dtos.Subscriptions;

/// <summary>A published calendar feed shown in a calendar. Only the feed's host is returned: a private feed address works
/// like a password.</summary>
public sealed class SubscriptionDto
{
    public required Guid Id { get; set; }

    public required Guid CalendarId { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>The colour its events draw in; null ⇒ the calendar's colour.</summary>
    public ItemColor? Color { get; set; }

    public required string SourceHost { get; set; }

    /// <summary>When the feed is next fetched; at or before now while a refresh is pending.</summary>
    public required DateTimeOffset NextRefreshAt { get; set; }

    /// <summary>Null until the first fetch attempt.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>Null until the first successful fetch.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Why the last fetch failed; null after a success.</summary>
    public SubscriptionError? LastError { get; set; }

    public required int EventCount { get; set; }
}
