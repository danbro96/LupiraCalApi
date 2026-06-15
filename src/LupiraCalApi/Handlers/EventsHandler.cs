using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.Events;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Handlers;

public sealed class EventsHandler(CurrentUser user, EventService events)
{
    public async Task<Results<Ok<List<EventOccurrenceDto>>, ProblemHttpResult, UnauthorizedHttpResult>> SearchAsync(
        string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, string? tag, string? metadata, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await events.SearchAsync(u.Id, query, from, to, calendarId, tag, metadata, ct));
    }

    public async Task<Results<Ok<EventDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreateEventRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await events.CreateAsync(u.Id, body, ct));
    }

    public async Task<Results<Ok<EventDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await events.GetAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<EventDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(Guid id, UpdateEventRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await events.UpdateAsync(u.Id, id, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await events.DeleteAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<EventDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AttachMetadataAsync(Guid id, JsonNode patch, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await events.AttachMetadataAsync(u.Id, id, patch, ct));
    }
}
