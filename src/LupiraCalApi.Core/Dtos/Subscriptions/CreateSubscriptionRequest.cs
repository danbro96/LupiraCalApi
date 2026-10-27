using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.Subscriptions;

public sealed class CreateSubscriptionRequest
{
    /// <summary>The published feed's address: http, https or webcal.</summary>
    public required string FeedUrl { get; set; }

    /// <summary>1–100 characters.</summary>
    public required string DisplayName { get; set; }

    public ItemColor? Color { get; set; }
}
