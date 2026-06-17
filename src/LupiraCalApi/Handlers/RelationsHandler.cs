using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Dtos.Relations;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class RelationsHandler(CurrentUser user, RelationService relations)
{
    public async Task<Results<Ok<RelationDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> LinkItemAsync(Guid id, CreateRelationRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await relations.LinkItemAsync(u.Id, id, body, ct));
    }

    public async Task<Results<Ok<List<RelationDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ListForItemAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await relations.ListForItemAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<List<CalendarItemDto>>, ProblemHttpResult, UnauthorizedHttpResult>> FindItemsLinkedToAsync(string toKind, string toRef, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await relations.FindItemsLinkedToAsync(u.Id, toKind, toRef, ct));
    }
}
