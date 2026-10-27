using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using Lupira.Sync;
using LupiraCalApi.Core.Application.Calendars;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Core.Application.Sync;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Subscriptions;
using LupiraCalApi.Core.Dtos.Sync;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

/// <summary>The offline-client sync surface: the paged items feed + the calendars and subscribed-events snapshots.</summary>
public sealed class SyncHandler(CurrentUser user, SyncFeed feed, CalendarService calendars, SubscriptionService subscriptions)
{
    public async Task<Results<Ok<SyncPage<ItemSyncChange>>, ProblemHttpResult, UnauthorizedHttpResult>> ItemsAsync(string? since, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await feed.ItemsAsync(u.Id, since, limit, ct));
    }

    public async Task<Results<Ok<SyncPage<ContainerDto>>, UnauthorizedHttpResult>> CalendarsAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var all = (await calendars.ListContainersAsync(u.Id, ct)).Value!;
        return TypedResults.Ok(new SyncPage<ContainerDto> { Cursor = "", HasMore = false, Reset = true, Changed = all, Deleted = [] });
    }

    public async Task<Results<Ok<SyncPage<SubscribedEventSyncDto>>, UnauthorizedHttpResult>> SubscribedEventsAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var all = await subscriptions.SyncEventsAsync(u.Id, ct);
        return TypedResults.Ok(new SyncPage<SubscribedEventSyncDto> { Cursor = string.Empty, HasMore = false, Reset = true, Changed = all, Deleted = [] });
    }

    public async Task<Results<Ok<List<ContainerDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ContainersAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await calendars.ListContainersAsync(u.Id, ct));
    }
}
