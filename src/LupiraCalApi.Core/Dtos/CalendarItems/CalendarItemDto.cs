using LupiraCalApi.Domain;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LupiraCalApi.Dtos.CalendarItems;

public sealed class CalendarMembershipDto
{
    public required Guid CalendarId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<CalendarEntryStatus>))]
    public required CalendarEntryStatus Status { get; set; }
}

public sealed class CalendarItemDto
{
    public required Guid Id { get; set; }
    public required string IcalUid { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ItemStatus>))]
    public ItemStatus? Status { get; set; }

    public required bool IsAllDay { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? RecurrenceRule { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ItemKind>))]
    public ItemKind? Kind { get; set; }

    public ItemKindDetails? KindDetails { get; set; }

    public Guid? PlaceId { get; set; }
    public Guid? ParentItemId { get; set; }
    public string[]? Tags { get; set; }
    public JsonNode? Metadata { get; set; }

    /// <summary>Event-bound payload (server-side only; never in ICS). At most one of <see cref="Prompt"/>/<see cref="Action"/> is set.</summary>
    public ItemPrompt? Prompt { get; set; }
    public ItemAction? Action { get; set; }

    /// <summary>How well-documented this item is (null = not applicable, e.g. exempt kinds/calendars). Drives Elicit ranking.</summary>
    public CompletenessScore? Completeness { get; set; }

    public required IReadOnlyList<CalendarMembershipDto> Calendars { get; set; }
    public required string Etag { get; set; }
}
