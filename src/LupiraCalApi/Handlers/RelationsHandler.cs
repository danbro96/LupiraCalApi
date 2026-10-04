using Lupira.Hosting.Problems;
using LupiraCalApi.Auth;
using LupiraCalApi.Core.Application.Items;
using LupiraCalApi.Core.Dtos.CalendarItems;
using LupiraCalApi.Core.Dtos.Relations;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class RelationsHandler(CurrentUser user, RelationService relations)
{
    public async Task<Results<Ok<RelationDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> LinkItemAsync(Guid id, CreateRelationRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await relations.LinkItemAsync(u.Id, id, body, ct));
    }

    public async Task<Results<Ok<List<RelationDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> LinkItemBatchAsync(
        Guid id, CreateRelationsBatchRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await relations.LinkItemBatchAsync(u.Id, id, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UnlinkItemAsync(Guid id, Guid relationId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await relations.UnlinkItemAsync(u.Id, id, relationId, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UnlinkItemBatchAsync(
        Guid id, DeleteRelationsBatchRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await relations.UnlinkItemBatchAsync(u.Id, id, body, ct));
    }

    public async Task<Results<Ok<List<RelationDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ListForItemAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await relations.ListForItemAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<List<RelationDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ListEdgesByKindAsync(string toKind, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await relations.ListEdgesByKindAsync(u.Id, toKind, ct));
    }

    public async Task<Results<Ok<List<CalendarItemDto>>, ProblemHttpResult, UnauthorizedHttpResult>> FindItemsLinkedToAsync(string toKind, string toRef, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await relations.FindItemsLinkedToAsync(u.Id, toKind, toRef, ct));
    }
}
