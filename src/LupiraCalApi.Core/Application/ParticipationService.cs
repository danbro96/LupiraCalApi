using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Mappers;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>First-class participation: invited / responded / attended / left, appended to the item's stream. The
/// embedded <see cref="ItemAttendee"/> read model composes the timestamps. Every attendee is a LupiraContactApi
/// contact, referenced by bare Guid and validated via <see cref="IContactResolver"/> when configured.</summary>
public sealed class ParticipationService(IDocumentSession session, AccessResolver access, CompletenessResolver completeness, IContactResolver contacts)
{
    public async Task<OpResult<CalendarItemDto>> InviteAsync(Guid principalId, Guid itemId, Guid contactId, string? role, CancellationToken ct = default)
    {
        // Fail-open: a null result means resolution is unavailable (unconfigured/transport) — proceed as before.
        // A non-null result missing the id is a definitive "no such contact".
        if (contacts.IsConfigured
            && await contacts.ResolveAsync([contactId], ct) is { } resolved
            && resolved.All(c => c.ContactId != contactId))
            return OpResult<CalendarItemDto>.Invalid("Unknown contact.");

        // Idempotent by contact: a contact already holding a (non-removed) participation row is not re-invited.
        return await AppendAsync(principalId, itemId, item => item.Attendees.Any(a => a.ContactId == contactId)
            ? null
            : new AttendeeInvited(itemId, Guid.NewGuid(), contactId, ParseRole(role), DateTimeOffset.UtcNow), ct);
    }

    public Task<OpResult<CalendarItemDto>> RespondAsync(Guid principalId, Guid itemId, Guid participationId, string? status, CancellationToken ct = default) =>
        AppendAsync(principalId, itemId, _ => new InvitationResponded(itemId, participationId, ParseStat(status), DateTimeOffset.UtcNow), ct);

    public Task<OpResult<CalendarItemDto>> ConfirmAttendanceAsync(Guid principalId, Guid itemId, Guid participationId, CancellationToken ct = default) =>
        AppendAsync(principalId, itemId, _ => new AttendanceConfirmed(itemId, participationId, DateTimeOffset.UtcNow), ct);

    public Task<OpResult<CalendarItemDto>> MarkLeftAsync(Guid principalId, Guid itemId, Guid participationId, CancellationToken ct = default) =>
        AppendAsync(principalId, itemId, _ => new ParticipantLeft(itemId, participationId, DateTimeOffset.UtcNow), ct);

    public Task<OpResult<CalendarItemDto>> RemoveAsync(Guid principalId, Guid itemId, Guid participationId, CancellationToken ct = default) =>
        AppendAsync(principalId, itemId, _ => new AttendeeRemoved(itemId, participationId), ct);

    private async Task<OpResult<CalendarItemDto>> AppendAsync(Guid principalId, Guid itemId, Func<CalendarItem, object?> makeEvent, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<CalendarItem>(itemId, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        if (!await access.CanWriteItemAsync(principalId, item, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this item.");
        if (makeEvent(item) is { } evt)
        {
            stream.AppendOne(evt);
            await session.SaveChangesAsync(ct);
        }
        var updated = await session.LoadAsync<CalendarItem>(itemId, ct);
        return OpResult<CalendarItemDto>.Ok(updated!.ToResponse(await completeness.ScoreItemAsync(updated!, ct)));
    }

    private static ParticipationRole ParseRole(string? s) => Enum.TryParse<ParticipationRole>(s, true, out var v) ? v : ParticipationRole.RequiredParticipant;
    private static ParticipationStatus ParseStat(string? s) => Enum.TryParse<ParticipationStatus>(s, true, out var v) ? v : ParticipationStatus.NeedsAction;
}
