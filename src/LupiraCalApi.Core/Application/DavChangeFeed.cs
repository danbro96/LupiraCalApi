using LupiraCalApi.Domain;
using Marten;

namespace LupiraCalApi.Application;

/// <summary>An item whose state changed since a sync token: its resource UID and current ETag, or a tombstone.</summary>
public sealed record DavChange(string Uid, string? Etag, bool Deleted);

/// <summary>The CalDAV change feed backing the <c>/dav-backend</c> seam: sync tokens are Marten's global event
/// sequence (opaque to the gateway), changes are the item streams touched past a token, and an item that was
/// deleted or is no longer accepted in the calendar surfaces as a tombstone.</summary>
public sealed class DavChangeFeed(IQuerySession session)
{
    /// <summary>The current sync token = the store's latest global event sequence.</summary>
    public async Task<long> CurrentTokenAsync(CancellationToken ct = default)
    {
        var last = await session.Events.QueryAllRawEvents().OrderByDescending(e => e.Sequence).Take(1).ToListAsync(ct);
        return last.Count > 0 ? last[0].Sequence : 0L;
    }

    /// <summary>All live items accepted into a calendar — the collection a Depth:1 listing enumerates.</summary>
    public async Task<List<CalendarItem>> AcceptedItemsAsync(Guid calendarId, CancellationToken ct = default)
    {
        var live = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        return [.. live.Where(i => i.IsAcceptedIn(calendarId))];
    }

    /// <summary>Changes in a calendar since <paramref name="since"/>; a null/unparsable token yields the
    /// full live listing (self-healing resync). Deletions and membership removals surface as tombstones
    /// only on incremental diffs; an item that was never in this calendar is skipped.</summary>
    public async Task<(long Token, IReadOnlyList<DavChange> Changes)> ChangesSinceAsync(Guid calendarId, long? since, CancellationToken ct = default)
    {
        var newToken = await CurrentTokenAsync(ct);

        if (since is null)
        {
            var live = await AcceptedItemsAsync(calendarId, ct);
            return (newToken, [.. live.Select(i => new DavChange(i.ExternalId, i.ContentHash, Deleted: false))]);
        }

        var changedIds = (await session.Events.QueryAllRawEvents().Where(e => e.Sequence > since).ToListAsync(ct))
            .Select(e => e.StreamId).Distinct().ToList();
        var items = await session.Query<CalendarItem>().Where(i => changedIds.Contains(i.Id)).ToListAsync(ct);

        var changes = new List<DavChange>();
        foreach (var i in items)
        {
            var membership = i.Calendars.FirstOrDefault(m => m.CalendarId == calendarId);
            if (membership is null) continue;   // never been in this calendar
            changes.Add(i.DeletedAt is not null || membership.Status != CalendarEntryStatus.Accepted
                ? new DavChange(i.ExternalId, null, Deleted: true)
                : new DavChange(i.ExternalId, i.ContentHash, Deleted: false));
        }
        return (newToken, changes);
    }
}
