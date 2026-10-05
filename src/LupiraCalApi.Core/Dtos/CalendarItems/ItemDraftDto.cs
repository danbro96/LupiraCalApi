using System.Text.Json.Serialization;
using LupiraCalApi.Core.Domain.Shared;

namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>An unsaved item read from a calendar file. Field names match <see cref="CreateCalendarItemRequest"/>.</summary>
public sealed class ItemDraftDto
{
    /// <summary>Your key for this event of the file; create with it as <c>SourceKey</c> so re-reading the same file creates nothing new.</summary>
    public required string SourceKey { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ItemStatus>))]
    public ItemStatus? Status { get; set; }

    public ItemCategory? Category { get; set; }

    public required bool IsAllDay { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    /// <summary>IANA zone of a timed item when the file names one.</summary>
    public string? StartTimezone { get; set; }

    public string? RecurrenceRule { get; set; }

    /// <summary>Free-text place label; resolve it to a place before creating the item.</summary>
    public string? Location { get; set; }
}
