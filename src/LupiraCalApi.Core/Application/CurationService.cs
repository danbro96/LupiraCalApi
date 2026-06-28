using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Mappers;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Curation of the many-to-many <c>CalendarItem ↔ Calendar</c> membership: list proposed items, accept/reject,
/// or file an existing item into a calendar. Authorized against the target calendar.</summary>
public sealed class CurationService(IDocumentSession session, AccessResolver access, CompletenessResolver completeness)
{
    public async Task<OpResult<List<CalendarItemDto>>> ListProposedAsync(Guid principalId, Guid calendarId, CancellationToken ct = default)
    {
        if (!await access.CanReadCalendarAsync(principalId, calendarId, ct)) return OpResult<List<CalendarItemDto>>.Forbidden("No access to this calendar.");
        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        var proposed = candidates.Where(i => i.Calendars.Any(m => m.CalendarId == calendarId && m.Status == CalendarEntryStatus.Proposed)).ToList();
        var scores = await completeness.ScoreItemsAsync(proposed, ct);
        return OpResult<List<CalendarItemDto>>.Ok([.. proposed.Select(i => i.ToResponse(scores[i.Id]))]);
    }

    public Task<OpResult<CalendarItemDto>> AcceptAsync(Guid principalId, Guid itemId, Guid calendarId, CancellationToken ct = default) =>
        MutateAsync(principalId, itemId, calendarId, new CalendarEntryStatusChanged(itemId, calendarId, CalendarEntryStatus.Accepted, DateTimeOffset.UtcNow), ct);

    public Task<OpResult<CalendarItemDto>> RejectAsync(Guid principalId, Guid itemId, Guid calendarId, CancellationToken ct = default) =>
        MutateAsync(principalId, itemId, calendarId, new RemovedFromCalendar(itemId, calendarId, DateTimeOffset.UtcNow), ct);

    public Task<OpResult<CalendarItemDto>> AddToCalendarAsync(Guid principalId, Guid itemId, Guid calendarId, string? status, CancellationToken ct = default) =>
        MutateAsync(principalId, itemId, calendarId, new AddedToCalendar(itemId, calendarId, ParseEntryStatus(status), DateTimeOffset.UtcNow), ct);

    private async Task<OpResult<CalendarItemDto>> MutateAsync(Guid principalId, Guid itemId, Guid calendarId, object @event, CancellationToken ct)
    {
        if (!await access.CanWriteCalendarAsync(principalId, calendarId, ct)) return OpResult<CalendarItemDto>.Forbidden("No write access to this calendar.");
        var stream = await session.Events.FetchForWriting<CalendarItem>(itemId, ct);
        var item = stream.Aggregate;
        if (item is null || item.DeletedAt is not null) return OpResult<CalendarItemDto>.NotFound();
        // Object-level guard. Item streams carry no owner, and read/write access is derived from membership, so a
        // bare CanWriteCalendar check would let a caller file ANY item into a calendar they control and thereby
        // self-grant access. Only allow curating an item already associated with this calendar (proposed/filed
        // here), an unclaimed item (no accepted membership anywhere), or one the caller can already read.
        var mayCurate = item.Calendars.Any(m => m.CalendarId == calendarId)
            || !item.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted)
            || await access.CanReadItemAsync(principalId, item, ct);
        if (!mayCurate) return OpResult<CalendarItemDto>.NotFound();
        stream.AppendOne(@event);
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<CalendarItem>(itemId, ct);
        return OpResult<CalendarItemDto>.Ok(updated!.ToResponse(await completeness.ScoreItemAsync(updated!, ct)));
    }

    private static CalendarEntryStatus ParseEntryStatus(string? s) => Enum.TryParse<CalendarEntryStatus>(s, true, out var v) ? v : CalendarEntryStatus.Proposed;
}
