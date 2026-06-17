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

    public async Task<Results<Ok<OwnerGrantDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GrantAddressBookOwnerAsync(Guid addressBookId, GrantOwnerRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await calendars.GrantAddressBookOwnerAsync(u.Id, addressBookId, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RevokeAddressBookOwnerAsync(Guid addressBookId, string email, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await calendars.RevokeAddressBookOwnerAsync(u.Id, addressBookId, email, ct));
    }
}
