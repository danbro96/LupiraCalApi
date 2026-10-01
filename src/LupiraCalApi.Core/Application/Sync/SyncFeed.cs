using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Application.Results;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.Sync;
using LupiraCalApi.Core.Mappers;
using Marten;

namespace LupiraCalApi.Core.Application.Sync;

/// <summary>
/// The offline-client changes feed: items filed (any status) to a calendar the caller can read, paged by
/// <c>UpdatedSequence</c> (index-backed). Unfiles and deletes surface as tombstones on deltas; an access change
/// restarts the stream (<see cref="SyncCursor"/>). Pre-watermark documents need one <c>--rebuild-items</c>.
/// </summary>
public sealed class SyncFeed(IQuerySession session, AccessResolver access, CompletenessResolver completeness)
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 500;

    public async Task<OpResult<SyncChangesResponse>> ChangesAsync(Guid principalId, string? since, int? limit, CancellationToken ct = default)
    {
        SyncCursor? given = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!SyncCursor.TryParse(since, out var parsed))
                return OpResult<SyncChangesResponse>.Invalid("since must be a cursor previously returned by this endpoint (or omitted for a full sync).");
            given = parsed;
        }
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var readable = (await access.AccessibleCalendarIdsAsync(principalId, ct)).ToArray();
        var scope = SyncCursor.ScopeOf(readable);
        var reset = given?.Scope != scope;
        var cursor = reset ? 0 : given!.Value.Sequence;
        var fullSync = cursor == 0;

        // Memberships are re-statused, never dropped, so unfiled items still match and get tombstoned.
        var page = await session.Query<CalendarItem>()
            .Where(i => i.UpdatedSequence > cursor && i.Calendars.Any(m => readable.Contains(m.CalendarId)))
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
                && i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && readable.Contains(m.CalendarId));
            if (visibleLive) changed.Add(i);
            // Full sync replaces the mirror wholesale, so tombstones would be noise.
            else if (!fullSync) deleted.Add(i.Id);
        }

        var scores = await completeness.ScoreItemsAsync(changed, ct);
        var next = rows.Count > 0 ? rows[^1].UpdatedSequence : cursor;
        return OpResult<SyncChangesResponse>.Ok(new SyncChangesResponse
        {
            Cursor = new SyncCursor(next, scope).ToString(),
            HasMore = hasMore,
            Reset = reset,
            Changed = [.. changed.Select(i => new SyncChangeDto { Item = i.ToResponse(scores[i.Id]), Guards = SectionGuardsDto.From(i) })],
            Deleted = deleted,
        });
    }
}
