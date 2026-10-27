using Lupira.Primitives;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Domain.Calendars;
using LupiraCalApi.Core.Domain.Subscriptions;
using LupiraCalApi.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Dispatcher.Subscriptions;

/// <summary>
/// One refresh tick: lease a due batch of subscriptions (SKIP LOCKED, so dispatcher replicas never share one), fetch each
/// feed and replace the mirrored events that changed. A failed fetch keeps the last good events and backs off. Logs name
/// the subscription and the feed's host, never its address.
/// </summary>
public sealed class SubscriptionRefreshService(
    SubscriptionsDbContext db,
    Marten.IQuerySession session,
    SubscriptionFeedClient feeds,
    IOptions<SubscriptionOptions> options,
    IOptions<ItemTimeZoneOptions> zones,
    TimeProvider clock,
    ILogger<SubscriptionRefreshService> logger)
{
    private readonly SubscriptionOptions _opts = options.Value;

    public async Task<int> RunTickAsync(CancellationToken ct)
    {
        var claimed = await ClaimAsync(ct);
        foreach (var sub in claimed)
        {
            using var activity = FireDispatchWorker.Activity.StartActivity("subscription.refresh");
            activity?.SetTag("subscription.id", sub.Id);
            activity?.SetTag("subscription.source_host", HostOf(sub));
            try
            {
                await RefreshAsync(sub, ct);
                activity?.SetTag("subscription.error", sub.LastError?.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad feed never poisons the batch; its lease lapses and it is retried.
                logger.LogError(ex, "Refresh failed for subscription {SubscriptionId} ({SourceHost}).", sub.Id, HostOf(sub));
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity == sub || e.Entity is SubscribedEvent).ToList())
                    entry.State = EntityState.Detached;
            }
        }

        return claimed.Count;
    }

    private static string HostOf(CalendarSubscription sub) => SubscriptionFeedUrl.SourceHost(new Uri(sub.FeedUrl));

    // The app clock, not the database's: NextRefreshAt is written on it, so a just-requested refresh is due at once.
    private Task<List<CalendarSubscription>> ClaimAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Subscriptions.FromSql($"""
            UPDATE cal_subscriptions.subscription SET leased_until = {now + _opts.Lease}
            WHERE id IN (
                SELECT id FROM cal_subscriptions.subscription
                WHERE next_refresh_at <= {now} AND (leased_until IS NULL OR leased_until < {now})
                ORDER BY next_refresh_at
                LIMIT {_opts.BatchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).ToListAsync(ct);
    }

    private async Task RefreshAsync(CalendarSubscription sub, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        sub.LastAttemptAt = now;
        var result = await feeds.FetchAsync(new Uri(sub.FeedUrl), sub.FeedEtag, sub.FeedLastModified, ct);
        var hash = result.Body is { } body ? ContentHash.Of(body) : null;

        if (result.Outcome == FeedFetchOutcome.Failed)
        {
            Fail(sub, result.Error!.Value, now);
        }
        else if (result.Outcome == FeedFetchOutcome.NotModified || hash == sub.ContentHash)
        {
            Succeed(sub, result, now);
        }
        else if (await ReadAsync(sub, result.Body!, ct) is { } events)
        {
            await ReplaceEventsAsync(sub, events, ct);
            sub.ContentHash = hash;
            Succeed(sub, result, now);
        }
        else
        {
            Fail(sub, SubscriptionError.NotACalendar, now);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<SubscribedEvent>?> ReadAsync(CalendarSubscription sub, string body, CancellationToken ct)
    {
        var zone = (await session.LoadAsync<Calendar>(sub.CalendarId, ct))?.DefaultTimezone ?? zones.Value.DefaultTimezone;
        try
        {
            return SubscribedFeedReader.Read(sub.Id, body, zone);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task ReplaceEventsAsync(CalendarSubscription sub, IReadOnlyList<SubscribedEvent> events, CancellationToken ct)
    {
        var stored = await db.Events.Where(e => e.SubscriptionId == sub.Id).ToDictionaryAsync(e => e.Id, ct);
        foreach (var e in events)
        {
            if (!stored.Remove(e.Id, out var current))
            {
                db.Events.Add(e);
            }
            else if (current.ContentHash != e.ContentHash)
            {
                current.SourceKey = e.SourceKey;
                current.FirstStart = e.FirstStart;
                current.LastEnd = e.LastEnd;
                current.ContentHash = e.ContentHash;
                current.Series = e.Series;
            }
        }

        db.Events.RemoveRange(stored.Values);
        sub.EventCount = events.Count;
    }

    private void Succeed(CalendarSubscription sub, FeedFetchResult result, DateTimeOffset now)
    {
        if (result.Outcome == FeedFetchOutcome.Fetched)
        {
            sub.FeedEtag = result.Etag;
            sub.FeedLastModified = result.LastModified;
        }

        sub.LastSyncedAt = now;
        sub.LastError = null;
        sub.ConsecutiveFailures = 0;
        sub.NextRefreshAt = SubscriptionSchedule.NextAfterSuccess(now, _opts.RefreshInterval);
        sub.LeasedUntil = null;
        logger.LogInformation("Subscription {SubscriptionId} synced from {SourceHost}: {EventCount} events.", sub.Id, HostOf(sub), sub.EventCount);
    }

    private void Fail(CalendarSubscription sub, SubscriptionError error, DateTimeOffset now)
    {
        sub.LastError = error;
        sub.ConsecutiveFailures++;
        sub.NextRefreshAt = SubscriptionSchedule.NextAfterFailure(now, _opts.RefreshInterval, sub.ConsecutiveFailures, _opts.MaxBackoff);
        sub.LeasedUntil = null;
        logger.LogWarning("Subscription {SubscriptionId} refresh from {SourceHost} failed ({Error}, {Failures} in a row).", sub.Id, HostOf(sub), error, sub.ConsecutiveFailures);
    }
}
