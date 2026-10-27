using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using LupiraCalApi.Core.Application.Subscriptions;
using LupiraCalApi.Core.Dtos.Subscriptions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraCalApi.Handlers;

public sealed class SubscriptionsHandler(CurrentUser user, SubscriptionService subscriptions)
{
    public async Task<Results<Ok<List<SubscriptionDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(Guid calendarId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await subscriptions.ListAsync(u.Id, calendarId, ct));
    }

    public async Task<Results<Ok<SubscriptionDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(Guid calendarId, CreateSubscriptionRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await subscriptions.CreateAsync(u.Id, calendarId, body, ct));
    }

    public async Task<Results<Ok<SubscriptionDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(Guid calendarId, Guid subscriptionId, UpdateSubscriptionRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await subscriptions.UpdateAsync(u.Id, calendarId, subscriptionId, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid calendarId, Guid subscriptionId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await subscriptions.DeleteAsync(u.Id, calendarId, subscriptionId, ct));
    }

    public async Task<Results<Ok<SubscriptionDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RefreshAsync(Guid calendarId, Guid subscriptionId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await subscriptions.RequestRefreshAsync(u.Id, calendarId, subscriptionId, ct));
    }

    public async Task<Results<Ok<SubscribedEventDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetEventAsync(Guid calendarId, Guid subscriptionId, Guid eventId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await subscriptions.GetEventAsync(u.Id, calendarId, subscriptionId, eventId, ct));
    }
}
