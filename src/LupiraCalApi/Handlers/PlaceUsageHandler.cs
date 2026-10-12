using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using LupiraCalApi.Core.Application.Places;
using LupiraCalApi.Core.Dtos.Places;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class PlaceUsageHandler(CurrentUser user, PlaceUsageService usage)
{
    public async Task<Results<Ok<List<PlaceUsageDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(
        DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await usage.ListAsync(u.Id, from, to, calendarId, limit, ct));
    }
}
