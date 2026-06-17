using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

/// <summary>First-class participation: invite / respond / attend / leave / remove (every attendee is a Contact).</summary>
public sealed class ParticipationHandler(CurrentUser user, ParticipationService participation)
{
    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> InviteAsync(Guid id, Guid contactId, string? role, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await participation.InviteAsync(u.Id, id, contactId, role, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RespondAsync(Guid id, Guid participationId, string? status, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await participation.RespondAsync(u.Id, id, participationId, status, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ConfirmAsync(Guid id, Guid participationId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await participation.ConfirmAttendanceAsync(u.Id, id, participationId, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> LeaveAsync(Guid id, Guid participationId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await participation.MarkLeftAsync(u.Id, id, participationId, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RemoveAsync(Guid id, Guid participationId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await participation.RemoveAsync(u.Id, id, participationId, ct));
    }
}
