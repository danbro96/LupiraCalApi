using System.Globalization;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using Marten;

namespace LupiraCalApi.Core.Application.Dav;

/// <summary>The CalDAV change feed backing the <c>/dav-backend</c> seam: sync tokens are Marten's global event
/// sequence, prefixed with the <see cref="DavResyncEpoch"/> once one exists (opaque to the gateway); changes are the item
/// streams touched past a token, and an item that was deleted or is no longer accepted in the calendar surfaces as a
/// tombstone.</summary>
public sealed class DavChangeFeed(IQuerySession session)
{
    /// <summary>The current sync token: the store's latest global event sequence under the current epoch.</summary>
    public async Task<string> CurrentTokenAsync(CancellationToken ct = default) => Format(await EpochAsync(ct), await SequenceAsync(ct));

    /// <summary>Invalidates every outstanding sync token; call after stored ETags changed without new events.</summary>
    public static async Task AdvanceEpochAsync(IDocumentStore store, CancellationToken ct = default)
    {
        await using var session = store.LightweightSession();
        var epoch = await session.LoadAsync<DavResyncEpoch>(DavResyncEpoch.SingletonId, ct) ?? new DavResyncEpoch();
        epoch.Value++;
        session.Store(epoch);
        await session.SaveChangesAsync(ct);
    }

    private async Task<long> SequenceAsync(CancellationToken ct)
    {
        var last = await session.Events.QueryAllRawEvents().OrderByDescending(e => e.Sequence).Take(1).ToListAsync(ct);
        return last.Count > 0 ? last[0].Sequence : 0L;
    }

    private async Task<int> EpochAsync(CancellationToken ct) => (await session.LoadAsync<DavResyncEpoch>(DavResyncEpoch.SingletonId, ct))?.Value ?? 0;

    // Epoch 0 keeps the bare sequence, so tokens minted before epochs existed stay valid.
    private static string Format(int epoch, long sequence) =>
        epoch == 0 ? sequence.ToString(CultureInfo.InvariantCulture) : $"{epoch.ToString(CultureInfo.InvariantCulture)}.{sequence.ToString(CultureInfo.InvariantCulture)}";

    private static long? Parse(string? token, int epoch)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var dot = token.IndexOf('.', StringComparison.Ordinal);
        var (tokenEpoch, sequence) = dot < 0 ? ("0", token) : (token[..dot], token[(dot + 1)..]);
        return tokenEpoch == epoch.ToString(CultureInfo.InvariantCulture)
            && long.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;
    }

    /// <summary>All live items accepted into a calendar — the collection a Depth:1 listing enumerates.</summary>
    public async Task<List<CalendarItem>> AcceptedItemsAsync(Guid calendarId, CancellationToken ct = default)
    {
        var live = await session.Query<CalendarItem>().Where(i => i.DeletedAt == null).ToListAsync(ct);
        return [.. live.Where(i => i.IsAcceptedIn(calendarId))];
    }

    /// <summary>Changes in a calendar since <paramref name="token"/>; an absent, unparsable or earlier-epoch token yields
    /// the full live listing (self-healing resync). Deletions and membership removals surface as tombstones only on
    /// incremental diffs; an item that was never in this calendar is skipped.</summary>
    public async Task<(string Token, IReadOnlyList<DavChange> Changes)> ChangesSinceAsync(Guid calendarId, string? token, CancellationToken ct = default)
    {
        var epoch = await EpochAsync(ct);
        var newToken = Format(epoch, await SequenceAsync(ct));
        var since = Parse(token, epoch);

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
