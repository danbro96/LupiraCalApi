namespace LupiraCalApi.Core.Dtos.CalendarItems;

/// <summary>Slim result of <c>set_participants</c> — the additions and how many were already present. Deliberately not the
/// full item DTO (which would echo the whole, growing attendee list on every call).</summary>
public sealed class SetParticipantsResult
{
    public required Guid ItemId { get; set; }

    public required IReadOnlyList<ParticipationRef> Added { get; set; }

    public required int AlreadyPresent { get; set; }
}
