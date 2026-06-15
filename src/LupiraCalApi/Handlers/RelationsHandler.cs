using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.Events;
using LupiraCalApi.Dtos.Relations;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class RelationsHandler(CurrentUser user, RelationService relations)
{
    public async Task<Results<Ok<RelationDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> LinkEventAsync(Guid id, CreateRelationRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await relations.LinkEventAsync(u.Id, id, body, ct));
    }

    public async Task<Results<Ok<List<RelationDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ListForEventAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await relations.ListForEventAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<List<EventDto>>, ProblemHttpResult, UnauthorizedHttpResult>> FindEventsLinkedToAsync(string toKind, string toRef, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await relations.FindEventsLinkedToAsync(u.Id, toKind, toRef, ct));
    }
}
