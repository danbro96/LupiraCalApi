using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using LupiraCalApi.Core.Application.Calendars;
using LupiraCalApi.Core.Dtos.Calendars;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class CalendarsHandler(CurrentUser user, CalendarService calendars)
{
    public async Task<Results<Ok<List<ContainerDto>>, UnauthorizedHttpResult>> ListAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkOnly(await calendars.ListContainersAsync(u.Id, ct));
    }

    public async Task<Results<Ok<ContainerDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreateCalendarRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await calendars.CreateAsync(u.Id, body, ct));
    }

    public async Task<Results<Ok<ContainerDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(Guid calendarId, UpdateCalendarRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await calendars.UpdateAsync(u.Id, calendarId, body, ct));
    }

    public async Task<Results<Ok<OwnerGrantDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GrantCalendarOwnerAsync(Guid calendarId, GrantOwnerRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await calendars.GrantCalendarOwnerAsync(u.Id, calendarId, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RevokeCalendarOwnerAsync(Guid calendarId, string email, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await calendars.RevokeCalendarOwnerAsync(u.Id, calendarId, email, ct));
    }
}
