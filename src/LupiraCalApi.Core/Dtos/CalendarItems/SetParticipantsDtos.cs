namespace LupiraCalApi.Dtos.CalendarItems;

/// <summary>Add a set of contacts as attendees of an item in one call (add-only — does not remove existing attendees).
/// <c>Attended</c> also marks them attended (for historical/backfilled events); pass false for a live invite flow.</summary>
public sealed class SetParticipantsRequest
{
    public required List<Guid> ContactIds { get; set; }
    public bool Attended { get; set; } = true;
}

/// <summary>A newly added attendee: the contact and its assigned participation id.</summary>
public sealed record ParticipationRef(Guid ContactId, Guid ParticipationId);

/// <summary>Slim result of <c>set_participants</c> — the additions and how many were already present. Deliberately not the
/// full item DTO (which would echo the whole, growing attendee list on every call).</summary>
public sealed record SetParticipantsResult(Guid ItemId, IReadOnlyList<ParticipationRef> Added, int AlreadyPresent);
