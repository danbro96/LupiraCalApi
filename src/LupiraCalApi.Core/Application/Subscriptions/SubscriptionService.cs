using Lupira.Results;
using LupiraCalApi.Core.Auth;
using LupiraCalApi.Core.Data;
using LupiraCalApi.Core.Domain.Calendars;
using LupiraCalApi.Core.Domain.Subscriptions;
using LupiraCalApi.Core.Dtos.Subscriptions;
using LupiraCalApi.Core.Mappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LupiraCalApi.Core.Application.Subscriptions;

/// <summary>A calendar's subscriptions to published calendar feeds. Anyone who can read the calendar lists them and reads
/// their events; only an owner adds, changes, removes or refreshes one. Fetching is the dispatcher's job: a change here
/// only schedules it.</summary>
public sealed class SubscriptionService(
    Marten.IQuerySession session, AccessResolver access, SubscriptionsDbContext db, SubscribedOccurrenceReader reader, TimeProvider clock, IOptions<SubscriptionOptions> options)
{
    private const string OwnerOnly = "Only an owner may change a calendar's subscriptions.";
    private const string NoAccess = "No access to this calendar.";
    private const string InvalidName = "DisplayName must be 1–100 characters.";
    private const int MaxNameLength = 100;

    private readonly SubscriptionOptions _opts = options.Value;

    public async Task<OpResult<List<SubscriptionDto>>> ListAsync(Guid principalId, Guid calendarId, CancellationToken ct = default)
    {
        if (await session.LoadAsync<Calendar>(calendarId, ct) is null) return OpResult<List<SubscriptionDto>>.NotFound();
        if (!await access.CanReadCalendarAsync(principalId, calendarId, ct)) return OpResult<List<SubscriptionDto>>.Forbidden(NoAccess);

        var subs = await db.Subscriptions.AsNoTracking().Where(s => s.CalendarId == calendarId).OrderBy(s => s.DisplayName).ToListAsync(ct);
        return OpResult<List<SubscriptionDto>>.Ok([.. subs.Select(s => s.ToResponse())]);
    }

    public async Task<OpResult<SubscriptionDto>> CreateAsync(Guid principalId, Guid calendarId, CreateSubscriptionRequest r, CancellationToken ct = default)
    {
        if (await session.LoadAsync<Calendar>(calendarId, ct) is null) return OpResult<SubscriptionDto>.NotFound();
        if (!await access.IsCalendarOwnerAsync(principalId, calendarId, ct)) return OpResult<SubscriptionDto>.Forbidden(OwnerOnly);
        if (Name(r.DisplayName) is not { } name) return OpResult<SubscriptionDto>.Invalid(InvalidName);
        if (!SubscriptionFeedUrl.TryNormalize(r.FeedUrl, out var url, out var error)) return OpResult<SubscriptionDto>.Invalid(error);
        if (await db.Subscriptions.CountAsync(s => s.CalendarId == calendarId, ct) >= _opts.MaxPerCalendar)
            return OpResult<SubscriptionDto>.Invalid($"A calendar can have at most {_opts.MaxPerCalendar} subscriptions.");

        var now = clock.GetUtcNow();
        var sub = new CalendarSubscription
        {
            Id = Guid.NewGuid(),
            CalendarId = calendarId,
            DisplayName = name,
            Color = r.Color,
            FeedUrl = url.AbsoluteUri,
            CreatedBy = principalId,
            CreatedAt = now,
            NextRefreshAt = now,
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return OpResult<SubscriptionDto>.Ok(sub.ToResponse());
    }

    public async Task<OpResult<SubscriptionDto>> UpdateAsync(Guid principalId, Guid calendarId, Guid subscriptionId, UpdateSubscriptionRequest r, CancellationToken ct = default)
    {
        if (await FindAsync(calendarId, subscriptionId, ct) is not { } sub) return OpResult<SubscriptionDto>.NotFound();
        if (!await access.IsCalendarOwnerAsync(principalId, calendarId, ct)) return OpResult<SubscriptionDto>.Forbidden(OwnerOnly);
        if (Name(r.DisplayName) is not { } name) return OpResult<SubscriptionDto>.Invalid(InvalidName);

        Uri? url = null;
        if (r.FeedUrl is not null && !SubscriptionFeedUrl.TryNormalize(r.FeedUrl, out url, out var error)) return OpResult<SubscriptionDto>.Invalid(error);

        sub.DisplayName = name;
        sub.Color = r.Color;
        if (url is not null && url.AbsoluteUri != sub.FeedUrl)
        {
            sub.FeedUrl = url.AbsoluteUri;
            sub.NextRefreshAt = clock.GetUtcNow();
            sub.LastAttemptAt = null;
            sub.LastSyncedAt = null;
            sub.LastError = null;
            sub.ConsecutiveFailures = 0;
            sub.FeedEtag = null;
            sub.FeedLastModified = null;
            sub.ContentHash = null;
        }

        await db.SaveChangesAsync(ct);
        return OpResult<SubscriptionDto>.Ok(sub.ToResponse());
    }

    /// <summary>Removes the subscription and, by cascade, its events.</summary>
    public async Task<OpResult> DeleteAsync(Guid principalId, Guid calendarId, Guid subscriptionId, CancellationToken ct = default)
    {
        if (await FindAsync(calendarId, subscriptionId, ct) is not { } sub) return OpResult.NotFound();
        if (!await access.IsCalendarOwnerAsync(principalId, calendarId, ct)) return OpResult.Forbidden(OwnerOnly);

        db.Subscriptions.Remove(sub);
        await db.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    /// <summary>Schedules a fetch now, unless the feed was tried within the cooldown — then nothing changes.</summary>
    public async Task<OpResult<SubscriptionDto>> RequestRefreshAsync(Guid principalId, Guid calendarId, Guid subscriptionId, CancellationToken ct = default)
    {
        if (await FindAsync(calendarId, subscriptionId, ct) is not { } sub) return OpResult<SubscriptionDto>.NotFound();
        if (!await access.IsCalendarOwnerAsync(principalId, calendarId, ct)) return OpResult<SubscriptionDto>.Forbidden(OwnerOnly);

        var now = clock.GetUtcNow();
        if (SubscriptionSchedule.CanRefreshManually(sub.LastAttemptAt, now, _opts.ManualRefreshCooldown) && sub.NextRefreshAt > now)
        {
            sub.NextRefreshAt = now;
            await db.SaveChangesAsync(ct);
        }

        return OpResult<SubscriptionDto>.Ok(sub.ToResponse());
    }

    public async Task<OpResult<SubscribedEventDto>> GetEventAsync(Guid principalId, Guid calendarId, Guid subscriptionId, Guid eventId, CancellationToken ct = default)
    {
        if (await session.LoadAsync<Calendar>(calendarId, ct) is null) return OpResult<SubscribedEventDto>.NotFound();
        if (!await access.CanReadCalendarAsync(principalId, calendarId, ct)) return OpResult<SubscribedEventDto>.Forbidden(NoAccess);
        if (await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subscriptionId && s.CalendarId == calendarId, ct) is not { } sub)
            return OpResult<SubscribedEventDto>.NotFound();

        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId && e.SubscriptionId == subscriptionId, ct);
        return ev is null ? OpResult<SubscribedEventDto>.NotFound() : OpResult<SubscribedEventDto>.Ok(ev.ToResponse(sub));
    }

    /// <summary>Every subscribed event the caller can read, expanded over the sync window — the offline mirror's snapshot.</summary>
    public async Task<List<SubscribedEventSyncDto>> SyncEventsAsync(Guid principalId, CancellationToken ct = default)
    {
        var calendarIds = await access.AccessibleCalendarIdsAsync(principalId, ct);
        return await reader.SnapshotAsync([.. calendarIds], ct);
    }

    private static string? Name(string? raw) => raw?.Trim() is { Length: > 0 and <= MaxNameLength } name ? name : null;

    private Task<CalendarSubscription?> FindAsync(Guid calendarId, Guid subscriptionId, CancellationToken ct) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && s.CalendarId == calendarId, ct);
}
