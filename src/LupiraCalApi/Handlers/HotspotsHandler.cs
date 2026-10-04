using Lupira.Hosting.Problems;
using LupiraCalApi.Auth;
using LupiraCalApi.Core.Application.Hotspots;
using LupiraCalApi.Core.Dtos.Hotspots;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class HotspotsHandler(CurrentUser user, HotspotService hotspots)
{
    public async Task<Results<Ok<List<HotspotDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(
        DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, int? minDays, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await hotspots.ListAsync(u.Id, from, to, calendarId, minDays, limit, ct));
    }
}
