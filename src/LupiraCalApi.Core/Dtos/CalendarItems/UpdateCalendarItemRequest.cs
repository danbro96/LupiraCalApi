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

    /// <summary>Revise the start/end date confidence (see <see cref="CreateCalendarItemRequest.StartPrecision"/>). Omitted ⇒ kept.</summary>
    public Domain.DatePrecision? StartPrecision { get; set; }
    public Domain.DatePrecision? EndPrecision { get; set; }

    /// <summary>Re-nest under a parent item (or set for the first time). Must exist and be accessible; omitted ⇒ kept.</summary>
    public Guid? ParentItemId { get; set; }

    /// <summary>Reclassify the item (enum name). Changing the category drops the previous details.</summary>
    public string? Category { get; set; }
    public string[]? Tags { get; set; }

    /// <summary>Change the item's presence segment status.</summary>
    public Domain.AvailabilityStatus? Availability { get; set; }

    /// <summary>Composable detail to set/merge: a <c>Booking</c> and/or a <c>Travel</c> leg; a supplied member replaces that member wholesale.</summary>
    public ItemDetailsRequest? Details { get; set; }
}
