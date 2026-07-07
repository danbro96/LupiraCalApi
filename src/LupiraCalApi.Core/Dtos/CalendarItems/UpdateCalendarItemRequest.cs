namespace LupiraCalApi.Dtos.CalendarItems;

public sealed class UpdateCalendarItemRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? RecurrenceRule { get; set; }

    /// <summary>Reclassify the item (enum name). Changing the kind drops stale details of the previous kind.</summary>
    public string? Kind { get; set; }
    public string[]? Tags { get; set; }

    /// <summary>For <c>Availability</c> items: change the segment's status.</summary>
    public Domain.AvailabilityStatus? Availability { get; set; }

    /// <summary>Kind-specific detail to set/merge (flight number, provider, booking refs, …); place refs are free-text labels.</summary>
    public ItemKindDetailsRequest? KindDetails { get; set; }
}
