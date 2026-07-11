using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>Create an item via REST/MCP. <c>CalendarId</c> optional — when set, the item is accepted into that calendar;
/// when null, the item is created unfiled (e.g. an automated source) for later curation. <c>Location</c> is free text
/// resolved to a <see cref="LupiraCalApi.Domain.Place"/>. <c>Category</c>/<c>Status</c> are the enum names.</summary>
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
    public string? Category { get; set; }
    public string[]? Tags { get; set; }

    /// <summary>Confidence of the start/end date for a historical or backfilled item — the date is still a concrete day;
    /// this records that it is only known to the month/year/roughly. Omit for exact dates.</summary>
    public Domain.DatePrecision? StartPrecision { get; set; }
    public Domain.DatePrecision? EndPrecision { get; set; }

    /// <summary>Optional server-side annotations (e.g. import provenance) merged onto the item at creation — same store
    /// as <c>POST /items/{id}/metadata</c>, saving a second call. Never in ICS.</summary>
    public JsonObject? Metadata { get; set; }

    /// <summary>Sets the item's presence segment status (whole-day or timed via Starts/Ends) — availability lives on the availability calendar.</summary>
    public Domain.AvailabilityStatus? Availability { get; set; }

    /// <summary>Composable detail: a <c>Booking</c> (any category) and/or a <c>Travel</c> leg (a <c>Trip</c>); Travel place refs are free-text labels.</summary>
    public ItemDetailsRequest? Details { get; set; }
}
