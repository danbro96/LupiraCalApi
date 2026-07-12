using System.Text.Json.Nodes;

namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>Create an item via REST/MCP. <c>CalendarId</c> optional — when set, the item is accepted into that calendar;
/// when null, the item is created unfiled (e.g. an automated source) for later curation. <c>Location</c> is free text
/// resolved to a <see cref="LupiraCalApi.Domain.Place"/>. <c>Category</c>/<c>Status</c> are the enum names.</summary>
public sealed class CreateCalendarItemRequest
{
    public Guid? CalendarId { get; set; }

    /// <summary>Client-supplied provenance/idempotency key (e.g. an import <c>sourceKey</c>). When set, the item's stream id
    /// is derived from it (<see cref="LupiraCalApi.Domain.DeterministicGuid"/>), so re-creating with the same key is a no-op
    /// that returns the existing item — safe batch/import replay. Also becomes the item's external UID. Omit for a random uid.</summary>
    public string? SourceKey { get; set; }

    /// <summary>Nest this item under a parent (e.g. a trip's leg/sub-event). The parent must exist and be accessible to the caller.</summary>
    public Guid? ParentItemId { get; set; }

    /// <summary>Alternative to <see cref="ParentItemId"/> for batch imports: reference the parent by its <see cref="SourceKey"/>;
    /// the server resolves it to the parent's deterministic id. Used when parent + children are created in one batch. Ignored if <see cref="ParentItemId"/> is set.</summary>
    public string? ParentSourceKey { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>Free-text location resolved server-side to a LupiraGeoApi place (fail-closed if geo is up but can't resolve).
    /// Prefer <see cref="PlaceId"/> when you have already resolved/vetted the place (places-first imports) — then <see cref="Location"/>
    /// is used only as the display label.</summary>
    public string? Location { get; set; }

    /// <summary>A pre-resolved LupiraGeoApi place id. When set, it is attached directly (no geocoding, no fail-closed risk) and
    /// <see cref="Location"/>, if any, is kept as the label. Trust the caller resolved it via geo first.</summary>
    public Guid? PlaceId { get; set; }
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
