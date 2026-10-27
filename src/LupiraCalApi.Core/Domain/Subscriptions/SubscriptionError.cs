using System.Text.Json.Serialization;

namespace LupiraCalApi.Core.Domain.Subscriptions;

/// <summary>Why the last refresh of a subscription failed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubscriptionError>))]
public enum SubscriptionError
{
    Unreachable,
    Rejected,
    NotACalendar,
    TooLarge,
    Blocked,
}
