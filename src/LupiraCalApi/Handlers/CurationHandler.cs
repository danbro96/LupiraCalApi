using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

/// <summary>Curation of the many-to-many item↔calendar membership (propose / accept / reject).</summary>
public sealed class CurationHandler(CurrentUser user, CurationService curation)
{
    public async Task<Results<Ok<List<CalendarItemDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ListProposedAsync(Guid calendarId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await curation.ListProposedAsync(u.Id, calendarId, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AcceptAsync(Guid itemId, Guid calendarId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await curation.AcceptAsync(u.Id, itemId, calendarId, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RejectAsync(Guid itemId, Guid calendarId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await curation.RejectAsync(u.Id, itemId, calendarId, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AddToCalendarAsync(Guid itemId, Guid calendarId, string? status, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await curation.AddToCalendarAsync(u.Id, itemId, calendarId, status, ct));
    }
}
