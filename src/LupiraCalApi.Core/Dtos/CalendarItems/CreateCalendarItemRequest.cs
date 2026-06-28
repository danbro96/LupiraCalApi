namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>Create an item via REST/MCP. <c>CalendarId</c> optional — when set, the item is accepted into that calendar;
/// when null, the item is created unfiled (e.g. an automated source) for later curation. <c>Location</c> is free text
/// resolved to a <see cref="LupiraCalApi.Domain.Place"/>. <c>Kind</c>/<c>Status</c> are the enum names.</summary>
public sealed class CreateCalendarItemRequest
{
    public Guid? CalendarId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Status { get; set; }
    public bool IsAllDay { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? StartTimezone { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? RecurrenceRule { get; set; }
    public string? Kind { get; set; }
    public string[]? Tags { get; set; }

    /// <summary>When <c>Kind</c> is <c>Availability</c>, the segment's status (whole-day or timed via Starts/Ends).</summary>
    public Domain.AvailabilityStatus? Availability { get; set; }
}
