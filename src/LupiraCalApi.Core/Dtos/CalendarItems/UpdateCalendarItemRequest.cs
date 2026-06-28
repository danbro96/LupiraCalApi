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
    public string[]? Tags { get; set; }

    /// <summary>For <c>Availability</c> items: change the segment's status.</summary>
    public Domain.AvailabilityStatus? Availability { get; set; }
}
