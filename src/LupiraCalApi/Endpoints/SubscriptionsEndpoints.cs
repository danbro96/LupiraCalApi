using LupiraCalApi.Core.Dtos.Subscriptions;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class SubscriptionsEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/calendars/{calendarId:guid}/subscriptions").RequireAuthorization("ApiPolicy").WithTags("Subscriptions");

        group.MapGet("/", (Guid calendarId, SubscriptionsHandler h, CancellationToken ct) => h.ListAsync(calendarId, ct))
            .WithName("ListSubscriptions")
            .WithSummary("List the published calendar feeds shown in a calendar, with their sync status; any grant on the calendar. Only the feed's host is returned, never its address.")
            .Produces<List<SubscriptionDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", (Guid calendarId, CreateSubscriptionRequest body, SubscriptionsHandler h, CancellationToken ct) => h.CreateAsync(calendarId, body, ct))
            .WithName("CreateSubscription")
            .WithSummary("Subscribe a calendar to a published calendar feed (http, https or webcal address); owner-only. Its events appear read-only in the calendar after the first sync.")
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{subscriptionId:guid}", (Guid calendarId, Guid subscriptionId, UpdateSubscriptionRequest body, SubscriptionsHandler h, CancellationToken ct) =>
                h.UpdateAsync(calendarId, subscriptionId, body, ct))
            .WithName("UpdateSubscription")
            .WithSummary("Rename or recolour a subscription, or point it at a new feed address (which restarts its sync); owner-only.")
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{subscriptionId:guid}", (Guid calendarId, Guid subscriptionId, SubscriptionsHandler h, CancellationToken ct) => h.DeleteAsync(calendarId, subscriptionId, ct))
            .WithName("DeleteSubscription")
            .WithSummary("Remove a subscription and its events from the calendar; owner-only.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{subscriptionId:guid}/refresh", (Guid calendarId, Guid subscriptionId, SubscriptionsHandler h, CancellationToken ct) => h.RefreshAsync(calendarId, subscriptionId, ct))
            .WithName("RefreshSubscription")
            .WithSummary("Fetch the feed again soon; owner-only. Within a short cooldown of the last attempt nothing changes. Returns the current status.")
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{subscriptionId:guid}/events/{eventId:guid}", (Guid calendarId, Guid subscriptionId, Guid eventId, SubscriptionsHandler h, CancellationToken ct) =>
                h.GetEventAsync(calendarId, subscriptionId, eventId, ct))
            .WithName("GetSubscribedEvent")
            .WithSummary("Get one read-only event of a subscribed feed (the id search returns with origin kind Subscription); any grant on the calendar.")
            .Produces<SubscribedEventDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
