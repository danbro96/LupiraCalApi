namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Add a set of contacts as attendees of an item in one call (add-only — does not remove existing attendees).
/// <c>Attended</c>: omitted marks them attended only when the item has already ended (stamped at its end); true always
/// marks them attended; false never does.</summary>
public sealed class SetParticipantsRequest
{
    public required List<Guid> ContactIds { get; set; }

    public bool? Attended { get; set; }
}
