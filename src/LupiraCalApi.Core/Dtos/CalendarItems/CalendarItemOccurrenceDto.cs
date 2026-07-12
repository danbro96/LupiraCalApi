using LupiraCalApi.Domain;
using System.Text.Json.Serialization;

namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>A single concrete occurrence of an item within a search window (recurrences expanded).</summary>
public sealed class CalendarItemOccurrenceDto
{
    public required Guid Id { get; set; }
    public string? Title { get; set; }
    public Guid? PlaceId { get; set; }
    public string? LocationLabel { get; set; }
    public required bool IsAllDay { get; set; }
    public required DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }

    /// <summary>Accepted calendar memberships, limited to calendars the caller can read.</summary>
    public required Guid[] CalendarIds { get; set; }

    public ItemCategory? Category { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ItemStatus>))]
    public ItemStatus? Status { get; set; }

    public string[]? Tags { get; set; }

    /// <summary>Hierarchy link (e.g. the trip a leg belongs to). Title only when the caller can read the parent.</summary>
    public Guid? ParentItemId { get; set; }

    public string? ParentTitle { get; set; }

    /// <summary>Direct children visible to the caller, independent of the current filters.</summary>
    public required int ChildCount { get; set; }

    /// <summary>The item's own completeness (same across its occurrences; null = not applicable), so search results rank directly.</summary>
    public CompletenessScore? Completeness { get; set; }

    public required string Etag { get; set; }
}
