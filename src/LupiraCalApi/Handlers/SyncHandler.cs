using LupiraCalApi.Auth;
using LupiraCalApi.Core.Application.Calendars;
using LupiraCalApi.Core.Application.Sync;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Sync;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

/// <summary>The offline-client sync surface: the paged changes feed + the containers snapshot.</summary>
public sealed class SyncHandler(CurrentUser user, SyncFeed feed, CalendarService calendars)
{
    public async Task<Results<Ok<SyncChangesResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ChangesAsync(string? since, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await feed.ChangesAsync(u.Id, since, limit, ct));
    }

    public async Task<Results<Ok<List<ContainerDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ContainersAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await calendars.ListContainersAsync(u.Id, ct));
    }
}
