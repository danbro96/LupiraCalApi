using LupiraCalApi.Core.Dtos.Places;
using LupiraCalApi.Handlers;

namespace LupiraCalApi.Endpoints;

public static class PlaceUsageEndpoints
{
    public static IEndpointRouteBuilder MapPlaceUsage(this IEndpointRouteBuilder app)
    {
        app.MapGet("/place-usage", (DateTimeOffset? from, DateTimeOffset? to, Guid? calendarId, int? limit, PlaceUsageHandler h, CancellationToken ct) =>
                h.ListAsync(from, to, calendarId, limit, ct))
            .RequireAuthorization("ApiPolicy").WithTags("Places")
            .WithName("GetPlaceUsage")
            .WithSummary("Places your events used — as their location or a travel endpoint — derived at read time from non-cancelled occurrences in calendars you can read (or calendarId), recurrences expanded up to `to`. Merged-away place ids fold into their survivor. Per place: event count, last day, per-category counts and the top 5 attendees. Ordered by last day, then event count. Defaults: from = 3 years before now, to = now, limit 200 (max 500).")
            .Produces<List<PlaceUsageDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
