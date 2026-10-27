using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.Subscriptions;

/// <summary>A subscribed event for an offline mirror, with its occurrences already expanded over the sync window.</summary>
public sealed class SubscribedEventSyncDto
{
    public required Guid Id { get; set; }

    public required Guid SubscriptionId { get; set; }

    public required Guid CalendarId { get; set; }

    public required string Title { get; set; }

    public string? LocationLabel { get; set; }

    public required bool IsAllDay { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ItemStatus>))]
    public required ItemStatus Status { get; set; }

    /// <summary>The subscription's colour; null ⇒ the calendar's colour.</summary>
    public ItemColor? Color { get; set; }

    public required string Etag { get; set; }

    public required List<SubscribedOccurrenceDto> Occurrences { get; set; }
}
