namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>
/// Core-field update. Two merge conventions coexist: a plain nullable field means "omitted ⇒ kept" (the original
/// contract, unchanged for existing callers); fields that must also be *clearable* to null carry a paired
/// <c>*Provided</c> sentinel — when the sentinel is true the field's value (including null) is written verbatim.
/// Offline clients replay whole-section writes, so they set every sentinel and send the full core section.
/// </summary>
public sealed class UpdateCalendarItemRequest
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    public string? Status { get; set; }

    /// <summary>Re-anchor the location to a resolved LupiraGeoApi place; <see cref="Location"/> then carries the display
    /// label. Set <see cref="PlaceIdProvided"/> with a null value to clear. Free-text-only location changes are rejected
    /// (resolve via geo first).</summary>
    public Guid? PlaceId { get; set; }

    public bool PlaceIdProvided { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public bool StartsAtProvided { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public bool EndsAtProvided { get; set; }

    /// <summary>Switch between timed and all-day (null ⇒ kept). All-day items carry their span in StartDate/EndDate.</summary>
    public bool? IsAllDay { get; set; }

    public DateOnly? StartDate { get; set; }

    public bool StartDateProvided { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool EndDateProvided { get; set; }

    /// <summary>IANA timezone names annotating the timed start/end.</summary>
    public string? StartTimezone { get; set; }

    public bool StartTimezoneProvided { get; set; }

    public string? EndTimezone { get; set; }

    public bool EndTimezoneProvided { get; set; }

    /// <summary>RFC 5545 recurrence rule. Non-null sets it; to CLEAR recurrence send null with
    /// <see cref="RecurrenceRuleProvided"/> = true (a bare null keeps the existing rule).</summary>
    public string? RecurrenceRule { get; set; }

    public bool RecurrenceRuleProvided { get; set; }

    /// <summary>Revise the start/end date confidence (see <see cref="CreateCalendarItemRequest.StartPrecision"/>). Omitted ⇒ kept.</summary>
    public Domain.Shared.DatePrecision? StartPrecision { get; set; }

    public Domain.Shared.DatePrecision? EndPrecision { get; set; }

    /// <summary>Re-nest under a parent item (or set for the first time). Must exist and be accessible; omitted ⇒ kept.</summary>
    public Guid? ParentItemId { get; set; }

    /// <summary>Reclassify the item (enum name). Changing the category drops the previous details.</summary>
    public string? Category { get; set; }

    public string[]? Tags { get; set; }

    /// <summary>Change the item's presence segment status.</summary>
    public Domain.Shared.AvailabilityStatus? Availability { get; set; }

    /// <summary>Composable detail to set/merge: a <c>Booking</c> and/or a <c>Travel</c> leg; a supplied member replaces that member wholesale.</summary>
    public ItemDetailsRequest? Details { get; set; }

    /// <summary>Client wall-clock of the edit, for last-writer-wins conflict resolution of the core section.
    /// Omitted ⇒ server receive time (online callers never need it).</summary>
    public DateTimeOffset? OccurredAt { get; set; }
}
