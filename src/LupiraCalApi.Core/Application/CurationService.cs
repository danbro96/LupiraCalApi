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

    /// <summary>File many existing items into calendars in one call. Each entry runs through <see cref="AddToCalendarAsync"/>
    /// so it carries the same per-calendar authorization and opaque-404 IDOR guard. Never aborts the whole batch — returns a
    /// per-entry status (filed | notfound | forbidden | invalid | conflict), in input order.</summary>
    public async Task<OpResult<List<FileItemResult>>> AddToCalendarBatchAsync(Guid principalId, IReadOnlyList<FileItemRequest> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0) return OpResult<List<FileItemResult>>.Invalid("At least one entry is required.");
        if (entries.Count > CalendarItemService.MaxBatch) return OpResult<List<FileItemResult>>.Invalid($"At most {CalendarItemService.MaxBatch} entries per batch.");

        var results = new List<FileItemResult>(entries.Count);
        foreach (var e in entries)
        {
            var res = await AddToCalendarAsync(principalId, e.ItemId, e.CalendarId, e.Status, ct);
            results.Add(new FileItemResult(e.ItemId, e.CalendarId, StatusName(res.Status), res.Error));
        }
        return OpResult<List<FileItemResult>>.Ok(results);
    }

    private static string StatusName(OpStatus s) => s switch
    {
        OpStatus.Ok => "filed",
        OpStatus.NotFound => "notfound",
        OpStatus.Forbidden => "forbidden",
        OpStatus.Conflict => "conflict",
        _ => "invalid",
    };

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
