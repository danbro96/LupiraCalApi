using LupiraCalApi.Application;
using LupiraCalApi.Auth;
using LupiraCalApi.Dtos.CalendarItems;
using LupiraCalApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Nodes;

namespace LupiraCalApi.Handlers;

public sealed class CalendarItemsHandler(CurrentUser user, CalendarItemService items)
{
    public async Task<Results<Ok<List<CalendarItemOccurrenceDto>>, ProblemHttpResult, UnauthorizedHttpResult>> SearchAsync(
        string? query, DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, string? tag, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await items.SearchAsync(u.Id, query, from, to, calendarId, tag, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreateCalendarItemRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await items.CreateAsync(u.Id, body, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await items.GetAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(Guid id, UpdateCalendarItemRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await items.UpdateAsync(u.Id, id, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await items.DeleteAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AttachMetadataAsync(Guid id, JsonNode patch, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await items.AttachMetadataAsync(u.Id, id, patch, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetPromptAsync(Guid id, SetItemPromptRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await items.SetPromptAsync(u.Id, id, body.ToDomain(), ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ClearPromptAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await items.ClearPromptAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<CalendarItemDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetActionAsync(Guid id, SetItemActionRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await items.SetActionAsync(u.Id, id, body.ToDomain(), ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ClearActionAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await items.ClearActionAsync(u.Id, id, ct));
    }
}
