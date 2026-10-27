using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.Subscriptions;

public sealed class UpdateSubscriptionRequest
{
    /// <summary>1–100 characters.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Null ⇒ the calendar's colour.</summary>
    public ItemColor? Color { get; set; }

    /// <summary>A new feed address restarts the sync; null keeps the current one.</summary>
    public string? FeedUrl { get; set; }
}
