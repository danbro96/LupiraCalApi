using LupiraCalApi.Auth;
using LupiraCalApi.Domain;
using LupiraCalApi.Dtos.Sync;
using LupiraCalApi.Mappers;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>
/// The offline-client changes feed: account-wide (everything the caller can read), paged strictly by each item's
/// <c>UpdatedSequence</c> watermark (index-backed — one document query, never a raw-event scan). Deletions and
/// visibility losses (membership removed, calendar unshared) surface as tombstone ids on incremental pulls.
/// Requires the item projection to be rebuilt once after deploy (<c>--rebuild-items</c>) so pre-existing
/// documents carry a watermark.
/// </summary>
public sealed class SyncFeed(IQuerySession session, AccessResolver access, CompletenessResolver completeness)
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 500;

    public async Task<OpResult<SyncChangesResponse>> ChangesAsync(Guid principalId, string? since, int? limit, CancellationToken ct = default)
    {
        long cursor = 0;
        if (!string.IsNullOrWhiteSpace(since) && (!long.TryParse(since, out cursor) || cursor < 0))
            return OpResult<SyncChangesResponse>.Invalid("since must be a cursor previously returned by this endpoint (or omitted for a full sync).");
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var fullSync = cursor == 0;

        var visible = (await access.AccessibleCalendarIdsAsync(principalId, ct)).ToHashSet();

        var page = await session.Query<CalendarItem>()
            .Where(i => i.UpdatedSequence > cursor)
            .OrderBy(i => i.UpdatedSequence)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > take;
        var rows = hasMore ? page.Take(take).ToList() : page;

        var changed = new List<CalendarItem>();
        var deleted = new List<Guid>();
        foreach (var i in rows)
        {
            var visibleLive = i.DeletedAt is null
                && i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && visible.Contains(m.CalendarId));
            if (visibleLive) changed.Add(i);
            // Full sync replaces the mirror wholesale, so tombstones would be noise; bare ids leak nothing.
            else if (!fullSync) deleted.Add(i.Id);
        }

        var scores = await completeness.ScoreItemsAsync(changed, ct);
        var next = rows.Count > 0 ? rows[^1].UpdatedSequence : cursor;
        return OpResult<SyncChangesResponse>.Ok(new SyncChangesResponse
        {
            Cursor = next.ToString(),
            HasMore = hasMore,
            Changed = [.. changed.Select(i => new SyncChangeDto { Item = i.ToResponse(scores[i.Id]), Guards = SectionGuardsDto.From(i) })],
            Deleted = deleted,
        });
    }
}
