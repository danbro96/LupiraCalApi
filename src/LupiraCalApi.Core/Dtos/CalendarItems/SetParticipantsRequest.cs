namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Add a set of contacts as attendees of an item in one call (add-only — does not remove existing attendees).
/// <c>Attended</c> also marks them attended (for historical/backfilled events); pass false for a live invite flow.</summary>
public sealed class SetParticipantsRequest
{
    public required List<Guid> ContactIds { get; set; }

    public bool Attended { get; set; } = true;
}
