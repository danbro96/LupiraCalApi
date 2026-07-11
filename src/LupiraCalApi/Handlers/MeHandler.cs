using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Dtos.Me;
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

    public async Task<Results<Ok<List<ContainerDto>>, UnauthorizedHttpResult>> BootstrapAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkOnly(await calendars.BootstrapPersonalAsync(u.Id, ct));
    }
}
