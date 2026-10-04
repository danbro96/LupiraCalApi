using LupiraCalApi.Auth;
using LupiraCalApi.Core.Application.Calendars;
using LupiraCalApi.Core.Dtos.Calendars;
using LupiraCalApi.Core.Dtos.Me;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class MeHandler(CurrentUser user, CalendarService calendars)
{
    public async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> GetAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(new MeDto { PrincipalId = u.Id, Email = u.Email, DisplayName = u.DisplayName });
    }

    public async Task<Results<Ok<List<ContainerDto>>, ProblemHttpResult, UnauthorizedHttpResult>> BootstrapAsync(BootstrapRequest? body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await calendars.BootstrapPersonalAsync(u.Id, body?.DefaultTimezone, ct));
    }
}
