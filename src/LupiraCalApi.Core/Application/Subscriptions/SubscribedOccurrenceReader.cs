using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Domain.CalendarItems;
using LupiraCalApi.Core.Domain.Shared;
using LupiraCalApi.Core.Domain.Subscriptions;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Core.Application.Subscriptions;

/// <summary>Projects subscribed events into occurrences at read time, like birthdays: the mirror stores each series once
/// and the shared <see cref="OccurrenceSlots"/> expand it exactly as a stored item's.</summary>
public sealed class SubscribedOccurrenceReader(SubscriptionsDbContext db, RecurrenceExpander expander, TimeProvider clock, IOptions<SubscriptionOptions> options)
{
    public async Task<List<CalendarItemOccurrenceDto>> OccurrencesAsync(
        Guid[] calendarIds, string? query, DateTimeOffset windowStart, DateTimeOffset windowEnd, DateTimeOffset expansionEnd, ItemStatus? status, CancellationToken ct = default)
    {
        var term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var results = new List<CalendarItemOccurrenceDto>();
        foreach (var (sub, e) in await EventsAsync(calendarIds, windowStart, windowEnd, ct))
        {
            var series = e.Series;
            if (status is not null && series.Status != status) continue;
            if (term is not null && !Matches(series, term)) continue;

            foreach (var slot in OccurrenceSlots.Of(series.Schedule(), expander, windowStart, windowEnd, expansionEnd))
            {
                var change = slot.Change;
                results.Add(new CalendarItemOccurrenceDto
                {
                    Id = e.Id,
                    Title = change?.Title ?? series.Title,
                    LocationLabel = change?.LocationLabel ?? series.LocationLabel,
                    IsAllDay = series.IsAllDay,
                    Start = slot.Start,
                    End = slot.End,
                    OriginalStart = slot.OriginalStart,
                    IsChanged = change is not null,
                    CalendarIds = [sub.CalendarId],
                    Color = sub.Color,
                    Status = change?.Status ?? series.Status,
                    ChildCount = 0,
                    Origin = new OccurrenceOrigin { Kind = OriginKind.Subscription, SourceId = sub.Id },
                    Etag = e.ContentHash,
                });
            }
        }

        return results;
    }

    /// <summary>Every event of the calendars' subscriptions, each with its occurrences over [now − SyncPast, now + SyncFuture).</summary>
    public async Task<List<SubscribedEventSyncDto>> SnapshotAsync(Guid[] calendarIds, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var (windowStart, windowEnd) = (now - options.Value.SyncPast, now + options.Value.SyncFuture);
        return [.. (await EventsAsync(calendarIds, windowStart, windowEnd, ct)).Select(x => new SubscribedEventSyncDto
        {
            Id = x.Event.Id,
            SubscriptionId = x.Subscription.Id,
            CalendarId = x.Subscription.CalendarId,
            Title = x.Event.Series.Title,
            LocationLabel = x.Event.Series.LocationLabel,
            IsAllDay = x.Event.Series.IsAllDay,
            Status = x.Event.Series.Status,
            Color = x.Subscription.Color,
            Etag = x.Event.ContentHash,
            Occurrences = [.. OccurrenceSlots.Of(x.Event.Series.Schedule(), expander, windowStart, windowEnd, windowEnd)
                .Select(s => new SubscribedOccurrenceDto { Start = s.Start, End = s.End, OriginalStart = s.OriginalStart })],
        })];
    }

    private static bool Matches(SubscribedSeries series, string term)
    {
        bool Has(string? text) => text is not null && text.Contains(term, StringComparison.OrdinalIgnoreCase);
        return Has(series.Title) || Has(series.Description) || (series.OccurrenceOverrides ?? []).Any(o => Has(o.Title) || Has(o.Description));
    }

    private async Task<List<(CalendarSubscription Subscription, SubscribedEvent Event)>> EventsAsync(
        Guid[] calendarIds, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        if (calendarIds.Length == 0) return [];
        var subs = await db.Subscriptions.AsNoTracking().Where(s => calendarIds.Contains(s.CalendarId)).ToDictionaryAsync(s => s.Id, ct);
        if (subs.Count == 0) return [];

        var subIds = subs.Keys.ToArray();
        var events = db.Events.AsNoTracking().Where(e => subIds.Contains(e.SubscriptionId));
        // Postgres takes UTC instants only; an unbounded edge needs no predicate.
        if (windowEnd != DateTimeOffset.MaxValue)
        {
            var end = windowEnd.ToUniversalTime();
            events = events.Where(e => e.FirstStart < end);
        }

        if (windowStart != DateTimeOffset.MinValue)
        {
            var start = windowStart.ToUniversalTime();
            events = events.Where(e => e.LastEnd == null || e.LastEnd >= start);
        }

        return [.. (await events.ToListAsync(ct)).Select(e => (subs[e.SubscriptionId], e))];
    }
}
