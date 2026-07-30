using System.Text.Json.Serialization;
using LupiraCalApi.Domain;

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

    /// <summary>Provenance of a read-time-projected occurrence (e.g. Birthdays → a contact). Null for stored items.</summary>
    public OccurrenceOrigin? Origin { get; set; }

    public required string Etag { get; set; }
}

/// <summary>What a read-time-projected occurrence was synthesized from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OriginKind>))]
public enum OriginKind { Birthday }

/// <summary>Ties a projected occurrence back to the entity it was derived from — the birthday occurrence's
/// <see cref="SourceId"/> is the contact whose birthday it is.</summary>
public sealed class OccurrenceOrigin
{
    public required OriginKind Kind { get; set; }
    public required Guid SourceId { get; set; }
}
