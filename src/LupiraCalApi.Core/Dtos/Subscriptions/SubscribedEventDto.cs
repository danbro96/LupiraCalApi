using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.Subscriptions;

/// <summary>One read-only event of a subscribed feed. <see cref="Start"/>/<see cref="End"/> are its first occurrence; an
/// all-day end is the inclusive last day at 00:00Z.</summary>
public sealed class SubscribedEventDto
{
    public required Guid Id { get; set; }

    public required Guid SubscriptionId { get; set; }

    public required string SubscriptionName { get; set; }

    public required Guid CalendarId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public string? LocationLabel { get; set; }

    public required bool IsAllDay { get; set; }

    public required DateTimeOffset Start { get; set; }

    public DateTimeOffset? End { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ItemStatus>))]
    public required ItemStatus Status { get; set; }

    public required bool IsRecurring { get; set; }
}
