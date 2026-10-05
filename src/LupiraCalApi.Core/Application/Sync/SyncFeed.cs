using Lupira.Results;
using Lupira.Sync;
using Lupira.Sync.Marten;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Dtos.Sync;
using LupiraCalApi.Core.Mappers;
using Marten;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Core.Application.Sync;

/// <summary>
/// The offline-client items feed: live items with an accepted filing in a calendar the caller can read. A full sync
/// pages them by id up to the head sequence; a delta returns the item streams changed since the cursor, with
/// unfiled and deleted ones as tombstones. An access change restarts the stream (<see cref="SyncCursor"/>).
/// </summary>
public sealed class SyncFeed(IQuerySession session, AccessResolver access, CompletenessResolver completeness, IOptions<SyncFeedOptions> options)
{
    private readonly TimeSpan _settleLag = options.Value.SettleLag;

    public async Task<OpResult<SyncPage<ItemSyncChange>>> ItemsAsync(Guid principalId, string? since, int? limit, CancellationToken ct = default)
    {
        if (!SyncFeedQuery.TryParse(since, limit, out var query))
            return OpResult<SyncPage<ItemSyncChange>>.Invalid(SyncFeedQuery.InvalidSince);

        var readable = (await access.AccessibleCalendarIdsAsync(principalId, ct)).ToArray();
        var scope = SyncCursor.ScopeOf(readable);
        var page = query.IsFullSync(scope)
            ? await FullSyncAsync(query, readable, scope, ct)
            : await DeltaAsync(query, readable, scope, ct);
        return OpResult<SyncPage<ItemSyncChange>>.Ok(page);
    }

    private async Task<SyncPage<ItemSyncChange>> FullSyncAsync(SyncFeedQuery query, Guid[] readable, string scope, CancellationToken ct)
    {
        var reset = query.IsReset(scope);
        var head = reset ? await session.HeadSequenceAsync(_settleLag, ct) : query.Since!.Value.Sequence;
        var after = reset ? null : query.Since!.Value.After;

        var visible = session.Query<CalendarItem>()
            .Where(i => i.DeletedAt == null && i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && readable.Contains(m.CalendarId)));
        if (after is { } last) visible = visible.Where(i => i.Id > last);
        var rows = await visible.OrderBy(i => i.Id).Take(query.Limit + 1).ToListAsync(ct);

        var hasMore = rows.Count > query.Limit;
        var items = hasMore ? rows.Take(query.Limit).ToList() : [.. rows];
        var cursor = new SyncCursor(head, scope) { After = hasMore ? items[^1].Id : null };
        return await PageAsync(cursor, hasMore, reset, items, [], ct);
    }

    private async Task<SyncPage<ItemSyncChange>> DeltaAsync(SyncFeedQuery query, Guid[] readable, string scope, CancellationToken ct)
    {
        var changes = await session.ChangedStreamsAsync<CalendarItem>(query.Since!.Value.Sequence, query.Limit, _settleLag, ct);

        // Memberships are re-statused, never dropped, so an unfiled item stays a candidate and gets tombstoned.
        var candidates = await session.Query<CalendarItem>()
            .Where(i => changes.Ids.Contains(i.Id) && i.Calendars.Any(m => readable.Contains(m.CalendarId)))
            .ToListAsync(ct);

        var changed = candidates.Where(i => IsVisible(i, readable)).ToList();
        var deleted = candidates.Where(i => !IsVisible(i, readable)).Select(i => i.Id).ToList();
        return await PageAsync(new SyncCursor(changes.NextSequence, scope), changes.HasMore, false, changed, deleted, ct);
    }

    private async Task<SyncPage<ItemSyncChange>> PageAsync(SyncCursor cursor, bool hasMore, bool reset, List<CalendarItem> changed, List<Guid> deleted, CancellationToken ct)
    {
        var scores = await completeness.ScoreItemsAsync(changed, ct);
        return new SyncPage<ItemSyncChange>
        {
            Cursor = cursor.ToString(),
            HasMore = hasMore,
            Reset = reset,
            Changed = [.. changed.Select(i => new ItemSyncChange { Item = i.ToResponse(scores[i.Id]), Guards = SectionGuardsDto.From(i) })],
            Deleted = deleted,
        };
    }

    private static bool IsVisible(CalendarItem i, Guid[] readable) =>
        i.DeletedAt is null && i.Calendars.Any(m => m.Status == CalendarEntryStatus.Accepted && readable.Contains(m.CalendarId));
}
