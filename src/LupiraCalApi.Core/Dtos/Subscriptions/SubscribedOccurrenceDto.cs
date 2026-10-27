namespace LupiraCalApi.Core.Dtos.Subscriptions;

/// <summary>One occurrence of a subscribed event; an all-day end is the inclusive last day at 00:00Z.</summary>
public sealed class SubscribedOccurrenceDto
{
    public required DateTimeOffset Start { get; set; }

    public DateTimeOffset? End { get; set; }

    /// <summary>Its start in the unmodified series; null when the event doesn't recur.</summary>
    public DateTimeOffset? OriginalStart { get; set; }
}
