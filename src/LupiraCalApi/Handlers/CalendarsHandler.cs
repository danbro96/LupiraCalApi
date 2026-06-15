using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.Calendars;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class CalendarsHandler(CurrentUser user, CalendarService calendars)
{
    public async Task<Results<Ok<List<ContainerDto>>, UnauthorizedHttpResult>> ListAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkOnly(await calendars.ListContainersAsync(u.Id, ct));
    }

    public async Task<Results<Ok<ContainerDto>, UnauthorizedHttpResult>> CreateAsync(CreateCalendarRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkOnly(await calendars.CreateAsync(u.Id, body, ct));
    }
}
