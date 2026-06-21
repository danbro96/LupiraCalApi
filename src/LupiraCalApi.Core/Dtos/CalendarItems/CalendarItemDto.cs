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

    public Guid? PlaceId { get; set; }
    public Guid? ParentItemId { get; set; }
    public string[]? Tags { get; set; }
    public JsonNode? Metadata { get; set; }
    public required IReadOnlyList<CalendarMembershipDto> Calendars { get; set; }
    public required string Etag { get; set; }
}
