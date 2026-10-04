using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lupira.Contracts.Fires;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Completeness;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

public sealed class CalendarItemDto
{
    public required Guid Id { get; set; }

    public required string ExternalId { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ItemStatus>))]
    public ItemStatus? Status { get; set; }

    public required bool IsAllDay { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<DatePrecision>))]
    public DatePrecision? StartPrecision { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<DatePrecision>))]
    public DatePrecision? EndPrecision { get; set; }

    public string? StartTimezone { get; set; }

    public string? RecurrenceRule { get; set; }

    /// <summary>Recurring items: unmodified starts removed from the series.</summary>
    public DateTimeOffset[]? ExcludedOccurrences { get; set; }

    /// <summary>Recurring items: one-off starts added to the series.</summary>
    public DateTimeOffset[]? ExtraOccurrences { get; set; }

    /// <summary>Recurring items: occurrences that deviate from the series (null members inherit).</summary>
    public OccurrenceOverride[]? OccurrenceOverrides { get; set; }

    public ItemCategory? Category { get; set; }

    public ItemDetails? Details { get; set; }

    public Guid? PlaceId { get; set; }

    public string? LocationLabel { get; set; }

    public Guid? ParentItemId { get; set; }

    public string[]? Tags { get; set; }

    public required JsonObject Metadata { get; set; }

    /// <summary>Event-bound payload (server-side only). At most one of <see cref="Prompt"/>/<see cref="Action"/> is set.</summary>
    public ItemPrompt? Prompt { get; set; }

    public ItemAction? Action { get; set; }

    /// <summary>How well-documented this item is (null = not applicable, e.g. exempt kinds/calendars). Drives Elicit ranking.</summary>
    public CompletenessScore? Completeness { get; set; }

    public required IReadOnlyList<ItemAttendeeDto> Attendees { get; set; }

    public required IReadOnlyList<CalendarMembershipDto> Calendars { get; set; }

    public required string Etag { get; set; }

    /// <summary>Server-side timestamps (from the event timeline) + stream version — the ordering/versioning
    /// surface offline clients key on. <c>Etag</c> stays content-derived and is orthogonal.</summary>
    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public required int Version { get; set; }
}
