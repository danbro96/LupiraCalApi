using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Mappers;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>Curation of the many-to-many <c>CalendarItem ↔ Calendar</c> membership: list proposed items, accept/reject,
/// or file an existing item into a calendar. Authorized against the target calendar.</summary>
public sealed class CurationService(IDocumentSession session, AccessResolver access)
{
    public async Task<OpResult<List<CalendarItemDto>>> ListProposedAsync(Guid principalId, Guid calendarId, CancellationToken ct = default)
    {
        if (!await access.CanReadCalendarAsync(principalId, calendarId, ct)) return OpResult<List<CalendarItemDto>>.Forbidden("No access to this calendar.");
        var candidates = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        var proposed = candidates.Where(i => i.Calendars.Any(m => m.CalendarId == calendarId && m.Status == CalendarEntryStatus.Proposed));
        return OpResult<List<CalendarItemDto>>.Ok(proposed.Select(i => i.ToResponse()).ToList());
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
        stream.AppendOne(@event);
        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<CalendarItem>(itemId, ct);
        return OpResult<CalendarItemDto>.Ok(updated!.ToResponse());
    }

    private static CalendarEntryStatus ParseEntryStatus(string? s) => Enum.TryParse<CalendarEntryStatus>(s, true, out var v) ? v : CalendarEntryStatus.Proposed;
}
