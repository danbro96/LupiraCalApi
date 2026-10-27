namespace LupiraCalApi.Core.Domain.Subscriptions;

/// <summary>One event of a subscribed feed, replaced wholesale on each refresh. <see cref="LastEnd"/> is null for a
/// recurring series, so only <see cref="FirstStart"/> bounds it.</summary>
public sealed class SubscribedEvent
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public required string SourceKey { get; set; }

    public DateTimeOffset FirstStart { get; set; }

    public DateTimeOffset? LastEnd { get; set; }

    public required string ContentHash { get; set; }

    public required SubscribedSeries Series { get; set; }
}
