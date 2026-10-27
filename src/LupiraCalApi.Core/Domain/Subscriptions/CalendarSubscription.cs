using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Domain.Subscriptions;

/// <summary>A published calendar feed mirrored read-only into a calendar: everyone who can read the calendar sees its
/// events, and only owners manage it. The refresh state is operational, not event-sourced.</summary>
public sealed class CalendarSubscription
{
    public Guid Id { get; set; }

    public Guid CalendarId { get; set; }

    public required string DisplayName { get; set; }

    public ItemColor? Color { get; set; }

    /// <summary>Never leaves the server: a private feed address works like a password.</summary>
    public required string FeedUrl { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextRefreshAt { get; set; }

    public DateTimeOffset? LeasedUntil { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    public SubscriptionError? LastError { get; set; }

    public int ConsecutiveFailures { get; set; }

    public string? FeedEtag { get; set; }

    public DateTimeOffset? FeedLastModified { get; set; }

    public string? ContentHash { get; set; }

    public int EventCount { get; set; }
}
